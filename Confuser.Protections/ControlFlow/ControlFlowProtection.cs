﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Confuser.Core;
using dnlib.DotNet;
using Confuser.Protections.ControlFlow;

namespace Confuser.Protections
{
    public interface IControlFlowService
    {
        void ExcludeMethod(ConfuserContext context, MethodDef method);
    }

    class ControlFlowProtection : Protection, IControlFlowService
    {
        public const string _Id = "ctrl flow";
        public const string _FullId = "Ki.ControlFlow";
        public const string _ServiceId = "Ki.ControlFlow";

        protected override void Initialize(ConfuserContext context)
        {
            context.Registry.RegisterService(_ServiceId, typeof(IControlFlowService), this);
        }

        protected override void PopulatePipeline(ProtectionPipeline pipeline)
        {
            pipeline.InsertPreStage(PipelineStage.OptimizeMethods, new ControlFlowPhase(this));
        }

        public override string Name
        {
            get { return "Control Flow Protection"; }
        }

        public override string Description
        {
            get { return "This protection mangles the code in the methods so that decompilers cannot decompile the methods."; }
        }

        public override string Id
        {
            get { return _Id; }
        }

        public override string FullId
        {
            get { return _FullId; }
        }

        public override ProtectionPreset Preset
        {
            get { return ProtectionPreset.Normal; }
        }

        public void ExcludeMethod(ConfuserContext context, MethodDef method)
        {
            ProtectionParameters.GetParameters(context, method).Remove(this);
        }
    }
}
