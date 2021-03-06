﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Confuser.Core;
using Confuser.Core.Services;
using Confuser.Renamer;
using dnlib.DotNet;
using Confuser.Core.Helpers;
using Confuser.DynCipher;
using dnlib.DotNet.Emit;
using System.Diagnostics;

namespace Confuser.Protections.Resources
{
    class InjectPhase : ProtectionPhase
    {
        public InjectPhase(ResourceProtection parent)
            : base(parent)
        {
        }

        public override ProtectionTargets Targets
        {
            get { return ProtectionTargets.Methods; }
        }

        protected override void Execute(ConfuserContext context, ProtectionParameters parameters)
        {
            if (parameters.Targets.Any())
            {
                var compression = context.Registry.GetService<ICompressionService>();
                var name = context.Registry.GetService<INameService>();
                var marker = context.Registry.GetService<IMarkerService>();
                var rt = context.Registry.GetService<IRuntimeService>();
                var moduleCtx = new REContext()
                {
                    Random = context.Registry.GetService<IRandomService>().GetRandomGenerator(Parent.Id),
                    Context = context,
                    Module = context.CurrentModule,
                    Marker = marker,
                    DynCipher = context.Registry.GetService<IDynCipherService>(),
                    Name = name
                };

                // Extract parameters
                moduleCtx.Mode = parameters.GetParameter<Mode>(context, context.CurrentModule, "mode", Mode.Normal);

                switch (moduleCtx.Mode)
                {
                    case Mode.Normal:
                        moduleCtx.ModeHandler = new NormalMode();
                        break;
                    case Mode.Dynamic:
                        moduleCtx.ModeHandler = new DynamicMode();
                        break;
                    default:
                        throw new UnreachableException();
                }

                // Inject helpers
                var decomp = compression.GetRuntimeDecompressor(context.CurrentModule, member =>
                {
                    name.MarkHelper(member, marker);
                    if (member is MethodDef)
                        ProtectionParameters.GetParameters(context, (MethodDef)member).Remove(Parent);
                });
                InjectHelpers(context, compression, rt, moduleCtx);

                // Mutate codes
                MutateInitializer(moduleCtx, decomp);

                var cctor = context.CurrentModule.GlobalType.FindStaticConstructor();
                cctor.Body.Instructions.Insert(0, Instruction.Create(OpCodes.Call, moduleCtx.InitMethod));

                new MDPhase(moduleCtx).Hook();
            }
        }

        void InjectHelpers(ConfuserContext context, ICompressionService compression, IRuntimeService rt, REContext moduleCtx)
        {
            var members = InjectHelper.Inject(rt.GetRuntimeType("Confuser.Runtime.Resource"), context.CurrentModule.GlobalType, context.CurrentModule);
            foreach (var member in members)
            {
                if (member.Name == "Initialize")
                    moduleCtx.InitMethod = (MethodDef)member;
                moduleCtx.Name.MarkHelper(member, moduleCtx.Marker);
            }

            var dataType = new TypeDefUser("", moduleCtx.Name.RandomName(), context.CurrentModule.CorLibTypes.GetTypeRef("System", "ValueType"));
            dataType.Layout = TypeAttributes.ExplicitLayout;
            dataType.Visibility = TypeAttributes.NestedPrivate;
            dataType.IsSealed = true;
            dataType.ClassLayout = new ClassLayoutUser(1, 0);
            moduleCtx.DataType = dataType;
            context.CurrentModule.GlobalType.NestedTypes.Add(dataType);
            moduleCtx.Name.MarkHelper(dataType, moduleCtx.Marker);

            moduleCtx.DataField = new FieldDefUser(moduleCtx.Name.RandomName(), new FieldSig(dataType.ToTypeSig()))
            {
                IsStatic = true,
                HasFieldRVA = true,
                InitialValue = new byte[0],
                Access = FieldAttributes.CompilerControlled
            };
            context.CurrentModule.GlobalType.Fields.Add(moduleCtx.DataField);
            moduleCtx.Name.MarkHelper(moduleCtx.DataField, moduleCtx.Marker);
        }

        void MutateInitializer(REContext moduleCtx, MethodDef decomp)
        {
            moduleCtx.InitMethod.Body.SimplifyMacros(moduleCtx.InitMethod.Parameters);
            List<Instruction> instrs = moduleCtx.InitMethod.Body.Instructions.ToList();
            for (int i = 0; i < instrs.Count; i++)
            {
                Instruction instr = instrs[i];
                IMethod method = instr.Operand as IMethod;
                if (instr.OpCode == OpCodes.Call)
                {
                    if (method.DeclaringType.Name == "Mutation" &&
                       method.Name == "Crypt")
                    {
                        Instruction ldBlock = instrs[i - 2];
                        Instruction ldKey = instrs[i - 1];
                        Debug.Assert(ldBlock.OpCode == OpCodes.Ldloc && ldKey.OpCode == OpCodes.Ldloc);
                        instrs.RemoveAt(i);
                        instrs.RemoveAt(i - 1);
                        instrs.RemoveAt(i - 2);
                        instrs.InsertRange(i - 2, moduleCtx.ModeHandler.EmitDecrypt(moduleCtx.InitMethod, moduleCtx, (Local)ldBlock.Operand, (Local)ldKey.Operand));
                    }
                    else if (method.DeclaringType.Name == "Lzma" &&
                       method.Name == "Decompress")
                    {
                        instr.Operand = decomp;
                    }
                }
            }
            moduleCtx.InitMethod.Body.Instructions.Clear();
            foreach (var instr in instrs)
                moduleCtx.InitMethod.Body.Instructions.Add(instr);

            MutationHelper.ReplacePlaceholder(moduleCtx.InitMethod, arg =>
            {
                List<Instruction> repl = new List<Instruction>();
                repl.AddRange(arg);
                repl.Add(Instruction.Create(OpCodes.Dup));
                repl.Add(Instruction.Create(OpCodes.Ldtoken, moduleCtx.DataField));
                repl.Add(Instruction.Create(OpCodes.Call, moduleCtx.Module.Import(
                    typeof(System.Runtime.CompilerServices.RuntimeHelpers).GetMethod("InitializeArray"))));
                return repl.ToArray();
            });
            MutationHelper.InjectKeys(moduleCtx.InitMethod,
                new int[] { 0, 1 },
                new int[] { 0xdead, 0xbeef });
        }
    }
}
