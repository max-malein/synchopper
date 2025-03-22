using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using System;
using System.Drawing;
using System.IO;
using System.Linq;

namespace synchopper
{
    internal class AddReference
    {
        internal static void ImportFile(string path)
        {
            if (!File.Exists(path))
            {
                throw new Exception("File does not exist.");
            }

            var ghDoc = Grasshopper.Instances.ActiveCanvas.Document;

            if (ghDoc == null)
                return;

            var io = new GH_DocumentIO();
            if (io.Open(path))
            {
                var newDoc = io.Document;
                if (newDoc != null)
                {
                    var groupName = "_synchopper: " + path;
                    GH_Group group;
                    var existingGroup = ghDoc.Objects.FirstOrDefault(o => o.NickName == groupName) as GH_Group;
                    if (existingGroup is not null)
                    {
                        existingGroup.ObjectIDs.Clear();
                        group = existingGroup;
                    }
                    else
                    {
                        group = existingGroup ?? new GH_Group()
                        {
                            NickName = groupName,
                            Colour = Color.Pink,
                        };

                        newDoc.AddObject(group, false);
                    }                    

                    foreach (var obj in newDoc.Objects)
                    {
                        ghDoc.RemoveObject(obj, false);

                        var existingObject = ghDoc.FindObject(obj.InstanceGuid, false);
                        if (existingObject is not null)
                        {
                            ghDoc.RemoveObject(existingObject, false);
                        }
                        
                        ghDoc.AddObject(obj, false);
                        group.AddObject(obj.InstanceGuid);
                    }
                }

                ghDoc.NewSolution(true);
                Grasshopper.Instances.ActiveCanvas.Refresh();
            }
            else
            {
                throw new Exception("Failed to open file.");
            }
        }
    }
}
