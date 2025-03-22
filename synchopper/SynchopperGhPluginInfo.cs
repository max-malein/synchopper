using Grasshopper.Kernel;
using System;

namespace synchopper
{
    public class SynchopperGhPluginInfo: GH_AssemblyInfo
    {
        public override string AssemblyName => "Synchopper plugin";
        public override Guid Id => new ("3D8ACA31-1C26-4F6B-A45B-428EF065DF43");
        public override string AssemblyVersion => this.Assembly.GetName().Version.ToString();
    }
}
