﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using dnlib.DotNet;

namespace Confuser.Core
{
    /// <summary>
    /// Result of the marker.
    /// </summary>
    public class MarkerResult
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MarkerResult"/> class.
        /// </summary>
        /// <param name="modules">The modules.</param>
        /// <param name="packer">The packer.</param>
        public MarkerResult(IList<ModuleDefMD> modules, Packer packer)
        {
            this.Modules = modules;
            this.Packer = packer;
        }

        /// <summary>
        /// Gets a list of modules that is marked.
        /// </summary>
        /// <value>The list of modules.</value>
        public IList<ModuleDefMD> Modules { get; private set; }

        /// <summary>
        /// Gets the packer if exists.
        /// </summary>
        /// <value>The packer, or null if no packer exists.</value>
        public Packer Packer { get; private set; }
    }
}
