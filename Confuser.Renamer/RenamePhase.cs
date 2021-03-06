﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Confuser.Core;
using dnlib.DotNet;

namespace Confuser.Renamer
{
    class RenamePhase : ProtectionPhase
    {
        public RenamePhase(NameProtection parent)
            : base(parent)
        {
        }

        public override ProtectionTargets Targets
        {
            get { return ProtectionTargets.AllDefinitions; }
        }

        protected override void Execute(ConfuserContext context, ProtectionParameters parameters)
        {
            NameService service = (NameService)context.Registry.GetService<INameService>();

            context.Logger.Debug("Renaming...");
            foreach (var renamer in service.Renamers)
            {
                foreach (var def in parameters.Targets)
                    renamer.PreRename(context, service, def);
            }

            foreach (var def in parameters.Targets)
            {
                if (!service.CanRename(def))
                    continue;

                var mode = service.GetRenameMode(def);

                var references = service.GetReferences(def);
                bool cancel = false;
                foreach (var refer in references)
                {
                    cancel |= refer.ShouldCancelRename();
                    if (cancel) break;
                }
                if (cancel)
                    continue;

                def.Name = service.ObfuscateName(def.Name, mode);
                if (def is TypeDef)
                {
                    var typeDef = (TypeDef)def;
                    typeDef.Namespace = service.ObfuscateName(typeDef.Namespace, mode);
                }

                foreach (var refer in references.ToList())
                {
                    if (!refer.UpdateNameReference(context, service))
                    {
                        context.Logger.ErrorFormat("Failed to update name reference on '{0}'.", def);
                        throw new ConfuserException(null);
                    }
                }
            }
        }
    }
}
