using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Undo;
using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace synchopper
{
    internal class AddReference
    {
        private readonly static string _prefix = "_synchopper: ";

        internal static void Import()
        {
            using var openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "Grasshopper Files (*.gh;*.ghx)|*.gh;*.ghx";
            openFileDialog.Title = "Select a Grasshopper File";

            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {                
                ImportFile(openFileDialog.FileName);
            }
        }

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

                if (newDoc is null)
                {
                    Rhino.RhinoApp.WriteLine("Synchopper: Failed to read the file.");
                    return;
                }

                // register undo here


                var groupName = _prefix + path;
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

                // we don't need to import the objects from the group that references the current file
                var currentFileReferenceGroupName = _prefix + ghDoc.FilePath;
                var currentFileReferenceGroups = ghDoc.Objects
                    .Where(o => o is GH_Group && o.NickName == currentFileReferenceGroupName)
                    .Cast<GH_Group>()
                    .ToList();

                var skipObjects = currentFileReferenceGroups
                    .SelectMany(g => g.Objects())
                    .ToHashSet();

                currentFileReferenceGroups.ForEach(g => skipObjects.Add(g)); // also skip the group itself

                var importObjects = newDoc.Objects.Where(o => !skipObjects.Contains(o)).ToList();

                if (importObjects.Count == 0)
                {
                    Rhino.RhinoApp.WriteLine("Synchopper: No objects to import.");
                    return;
                }

                var existingObjectsInSyncGroups = ghDoc.Objects
                    .Where(o => o is GH_Group && o.NickName.StartsWith(_prefix))
                    .Cast<GH_Group>()
                    .SelectMany(g => g.Objects())
                    .Select(o => o.InstanceGuid)
                    .ToHashSet();

                var existingObjectsOutsideSyncGroups = ghDoc.Objects
                    .Select(o => o.InstanceGuid)
                    .Except(existingObjectsInSyncGroups)
                    .ToHashSet();

                var objectsToRemove = existingObjectsOutsideSyncGroups
                    .Intersect(importObjects.Select(o => o.InstanceGuid))
                    .ToList();

                if (objectsToRemove.Count > 0)
                {
                    var message = $"Synchopper: There are {objectsToRemove.Count} objects in the current file that will be replaced by the objects from the imported file.\n" +
                        $"Do you want to proceed?";

                    if (System.Windows.Forms.MessageBox.Show(message, "Synchopper", System.Windows.Forms.MessageBoxButtons.OKCancel) == System.Windows.Forms.DialogResult.Cancel)
                    {
                        return;
                    }

                    foreach (var obj in objectsToRemove)
                    {
                        var existingObject = ghDoc.FindObject(obj, false);
                        if (existingObject is not null)
                        {
                            ghDoc.RemoveObject(existingObject, false);
                        }
                    }
                }

                // remove objects in the current group if any.
                // they will be replaced
                foreach (var item in group.Objects())
                {
                    ghDoc.RemoveObject(item, false);
                }

                foreach (var obj in importObjects)
                {
                    ghDoc.AddObject(obj, false);
                    group.AddObject(obj.InstanceGuid);
                }

                ghDoc.NewSolution(true);
                Grasshopper.Instances.ActiveCanvas.Refresh();
            }
            else
            {
                Rhino.RhinoApp.WriteLine("Synchopper: Failed to open file.");
                return;
            }
        }
    }
}
