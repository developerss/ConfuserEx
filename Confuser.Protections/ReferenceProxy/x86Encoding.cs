﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using dnlib.DotNet.Emit;
using dnlib.DotNet;
using Confuser.Core;
using Confuser.Core.Services;
using Confuser.DynCipher;
using Confuser.DynCipher.Generation;
using Confuser.DynCipher.AST;
using Confuser.Renamer;
using dnlib.DotNet.Writer;

namespace Confuser.Protections.ReferenceProxy
{
    class x86Encoding : IRPEncoding
    {
        class CodeGen : CILCodeGen
        {
            Instruction[] arg;
            public CodeGen(Instruction[] arg, MethodDef method, IList<Instruction> instrs)
                : base(method, instrs)
            {
                this.arg = arg;
            }
            protected override void LoadVar(Variable var)
            {
                if (var.Name == "{RESULT}")
                {
                    foreach (var instr in arg)
                        base.Emit(instr);
                }
                else
                    base.LoadVar(var);
            }
        }

        void Compile(RPContext ctx, out Func<int, int> expCompiled, out MethodDef native)
        {
            Variable var = new Variable("{VAR}");
            Variable result = new Variable("{RESULT}");

            var int32 = ctx.Module.CorLibTypes.Int32;
            native = new MethodDefUser(ctx.Context.Registry.GetService<INameService>().RandomName(), MethodSig.CreateStatic(int32, int32), MethodAttributes.PinvokeImpl | MethodAttributes.PrivateScope | MethodAttributes.Static);
            native.ImplAttributes = MethodImplAttributes.Native | MethodImplAttributes.Unmanaged | MethodImplAttributes.PreserveSig;
            ctx.Module.GlobalType.Methods.Add(native);

            ctx.Context.Registry.GetService<IMarkerService>().Mark(native);
            ctx.Context.Registry.GetService<INameService>().SetCanRename(native, false);

            x86Register? reg;
            var codeGen = new x86CodeGen();
            Expression expression, inverse;
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

            var code = CodeGenUtils.AssembleCode(codeGen, reg.Value);

            expCompiled = new DMCodeGen(typeof(int), new[] { Tuple.Create("{VAR}", typeof(int)) })
                .GenerateCIL(expression)
                .Compile<Func<int, int>>();

            nativeCodes.Add(Tuple.Create(native, code, (dnlib.DotNet.Writer.MethodBody)null));
            if (!addedHandler)
            {
                ctx.Context.CurrentModuleWriterListener.OnWriterEvent += InjectNativeCode;
                addedHandler = true;
            }
        }

        bool addedHandler = false;
        void InjectNativeCode(object sender, ModuleWriterListenerEventArgs e)
        {
            ModuleWriter writer = (ModuleWriter)sender;
            if (e.WriterEvent == ModuleWriterEvent.MDEndWriteMethodBodies)
            {
                foreach (var native in nativeCodes)
                    native.Item3 = writer.MethodBodies.Add(new dnlib.DotNet.Writer.MethodBody(native.Item2));
            }
            else if (e.WriterEvent == ModuleWriterEvent.EndCalculateRvasAndFileOffsets)
            {
                foreach (var native in nativeCodes)
                {
                    var rid = writer.MetaData.GetRid(native.Item1);
                    writer.MetaData.TablesHeap.MethodTable[rid].RVA = (uint)native.Item3.RVA;
                }
            }
        }

        List<Tuple<MethodDef, byte[], dnlib.DotNet.Writer.MethodBody>> nativeCodes = new List<Tuple<MethodDef, byte[], dnlib.DotNet.Writer.MethodBody>>();
        Dictionary<MethodDef, Tuple<MethodDef, Func<int, int>>> keys = new Dictionary<MethodDef, Tuple<MethodDef, Func<int, int>>>();

        Tuple<MethodDef, Func<int, int>> GetKey(RPContext ctx, MethodDef init)
        {
            Tuple<MethodDef, Func<int, int>> ret;
            if (!keys.TryGetValue(init, out ret))
            {
                Func<int, int> keyFunc;
                MethodDef native;
                Compile(ctx, out keyFunc, out native);
                keys[init] = ret = Tuple.Create(native, keyFunc);
            }
            return ret;
        }

        public Instruction[] EmitDecode(MethodDef init, RPContext ctx, Instruction[] arg)
        {
            var key = GetKey(ctx, init);

            var repl = new List<Instruction>();
            repl.AddRange(arg);
            repl.Add(Instruction.Create(OpCodes.Call, key.Item1));
            return repl.ToArray();
        }

        public int Encode(MethodDef init, RPContext ctx, int value)
        {
            var key = GetKey(ctx, init);
            return key.Item2(value);
        }
    }
}
