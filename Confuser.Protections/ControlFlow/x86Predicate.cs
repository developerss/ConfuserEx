﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Confuser.DynCipher;
using Confuser.Core.Services;
using Confuser.DynCipher.AST;
using Confuser.DynCipher.Generation;
using Confuser.Core;
using dnlib.DotNet.Emit;
using dnlib.DotNet;
using dnlib.DotNet.Writer;
using System.IO;
using Confuser.Renamer;

namespace Confuser.Protections.ControlFlow
{
    class x86Predicate : IPredicate
    {
        class x86Encoding
        {
            Expression expression;
            Expression inverse;
            byte[] code;
            dnlib.DotNet.Writer.MethodBody codeChunk;

            public Func<int, int> expCompiled;
            public MethodDef native;

            public void Compile(CFContext ctx)
            {
                Variable var = new Variable("{VAR}");
                Variable result = new Variable("{RESULT}");

                var int32 = ctx.Method.Module.CorLibTypes.Int32;
                native = new MethodDefUser(ctx.Context.Registry.GetService<INameService>().RandomName(), MethodSig.CreateStatic(int32, int32), MethodAttributes.PinvokeImpl | MethodAttributes.PrivateScope | MethodAttributes.Static);
                native.ImplAttributes = MethodImplAttributes.Native | MethodImplAttributes.Unmanaged | MethodImplAttributes.PreserveSig;
                // Attempt to improve performance --- failed with StackOverflowException... :/
                //var suppressAttr = ctx.Method.Module.CorLibTypes.GetTypeRef("System.Security", "SuppressUnmanagedCodeSecurityAttribute").ResolveThrow();
                //native.CustomAttributes.Add(new CustomAttribute((MemberRef)ctx.Method.Module.Import(suppressAttr.FindDefaultConstructor())));
                //native.HasSecurity = true;
                ctx.Method.Module.GlobalType.Methods.Add(native);

                ctx.Context.Registry.GetService<IMarkerService>().Mark(native);
                ctx.Context.Registry.GetService<INameService>().SetCanRename(native, false);

                x86Register? reg;
                var codeGen = new x86CodeGen();
                do
                {
                    ctx.DynCipher.GenerateExpressionPair(
                        ctx.Random,
                        new VariableExpression() { Variable = var }, new VariableExpression() { Variable = result },
                        ctx.Depth, out expression, out inverse);

                    reg = codeGen.GenerateX86(inverse, (v, r) =>
                    {
                        return new[] { x86Instruction.Create(x86OpCode.POP, new x86RegisterOperand(r)) };
                    });
                } while (reg == null);

                code = CodeGenUtils.AssembleCode(codeGen, reg.Value);

                expCompiled = new DMCodeGen(typeof(int), new[] { Tuple.Create("{VAR}", typeof(int)) })
                    .GenerateCIL(expression)
                    .Compile<Func<int, int>>();


                ctx.Context.CurrentModuleWriterListener.OnWriterEvent += InjectNativeCode;
            }

            void InjectNativeCode(object sender, ModuleWriterListenerEventArgs e)
            {
                ModuleWriter writer = (ModuleWriter)sender;
                if (e.WriterEvent == ModuleWriterEvent.MDEndWriteMethodBodies)
                {
                    codeChunk = writer.MethodBodies.Add(new dnlib.DotNet.Writer.MethodBody(code));
                }
                else if (e.WriterEvent == ModuleWriterEvent.EndCalculateRvasAndFileOffsets)
                {
                    var rid = writer.MetaData.GetRid(native);
                    writer.MetaData.TablesHeap.MethodTable[rid].RVA = (uint)codeChunk.RVA;
                }
            }
        }

        CFContext ctx;
        public x86Predicate(CFContext ctx)
        {
            this.ctx = ctx;
        }

        static readonly object Encoding = new object();
        x86Encoding encoding;

        bool inited = false;
        public void Init(CilBody body)
        {
            if (inited)
                return;

            encoding = ctx.Context.Annotations.Get<x86Encoding>(ctx.Method.DeclaringType, Encoding, null);
            if (encoding == null)
            {
                encoding = new x86Encoding();
                encoding.Compile(ctx);
                ctx.Context.Annotations.Set(ctx.Method.DeclaringType, Encoding, encoding);
            }

            inited = true;
        }

        public void EmitSwitchLoad(IList<Instruction> instrs)
        {
            instrs.Add(Instruction.Create(OpCodes.Call, encoding.native));
        }

        public int GetSwitchKey(int key)
        {
            return encoding.expCompiled(key);
        }
    }
}
