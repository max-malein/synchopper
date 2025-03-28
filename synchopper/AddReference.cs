using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Undo;
using System;
using System.Collections.Generic;
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

                var objectsToDelete = new List<IGH_DocumentObject>();
                var objectsToAdd = new List<IGH_DocumentObject>();

                var groupName = _prefix + path;
                GH_Group group;
                var existingGroup = ghDoc.Objects.FirstOrDefault(o => o.NickName == groupName) as GH_Group;
                if (existingGroup is not null)
                {
                    objectsToDelete.AddRange(existingGroup.ObjectsRecursive());
                    group = existingGroup;
                }
                else
                {
                    group = existingGroup ?? new GH_Group()
                    {
                        NickName = groupName,
                        Colour = Color.FromArgb(100, Color.Pink),
                    };

                    objectsToAdd.Add(group);
                    //ghDoc.UndoUtil.RecordAddObjectEvent("AddGroupEvent", group);
                }

                // we don't need to import any referenced groups to avoid recursive import
                var sycnhopperGroups = newDoc.Objects
                    .Where(o => o is GH_Group && o.NickName.StartsWith(_prefix))
                    .Cast<GH_Group>()
                    .ToHashSet();

                var skipObjects = sycnhopperGroups
                    .SelectMany(g => g.ObjectsRecursive())
                    .ToHashSet();

                skipObjects.UnionWith(sycnhopperGroups); // also skip the groups itself 

                var importObjects = newDoc.Objects
                    .Except(skipObjects)
                    .ToList();

                if (importObjects.Count == 0)
                {
                    Rhino.RhinoApp.WriteLine("Synchopper: No objects to import.");
                    return;
                }

                objectsToAdd.AddRange(importObjects);

                // find existing objects outside of the sync groups that will be replaced
                var existingSyncGroups = ghDoc.Objects
                    .Where(o => o is GH_Group && o.NickName.StartsWith(_prefix))
                    .Cast<GH_Group>()
                    .ToHashSet();

                var existingObjectsInSyncGroups = existingSyncGroups
                    .SelectMany(g => g.ObjectsRecursive())
                    .ToHashSet();

                var existingObjectsOutsideSyncGroups = ghDoc.Objects
                    .Except(existingObjectsInSyncGroups)
                    .Except(existingSyncGroups)
                    .ToHashSet();

                var existingObjectsToDelete = existingObjectsOutsideSyncGroups
                    .Select(o => o.InstanceGuid).ToList()
                    .Intersect(importObjects.Select(o => o.InstanceGuid).ToList())
                    .ToList();

                if (existingObjectsToDelete.Count > 0)
                {
                    var message = $"Synchopper: There are {existingObjectsToDelete.Count} objects in the current file that will be replaced by the objects from the imported file.\n" +
                        $"Do you want to proceed?";

                    if (System.Windows.Forms.MessageBox.Show(message, "Synchopper", System.Windows.Forms.MessageBoxButtons.OKCancel) == System.Windows.Forms.DialogResult.Cancel)
                    {
                        return;
                    }

                    foreach (var existingObject in existingObjectsToDelete)
                    {
                        var exOb = ghDoc.Objects.FirstOrDefault(o => o.InstanceGuid == existingObject);
                        exOb.NewInstanceGuid();
                    }

                    //objectsToDelete.AddRange(existingObjectsToDelete);
                }


                // remove objects from the current file
                if (objectsToDelete.Count > 0)
                {
                    ghDoc.UndoUtil.RecordRemoveObjectEvent("Remove existing group objects", objectsToDelete);

                    foreach (var item in group.Objects())
                    {
                        ghDoc.RemoveObject(item, false);
                    }
                }                

                ghDoc.UndoUtil.RecordAddObjectEvent("Import new objects", importObjects);                

                foreach (var obj in objectsToAdd)
                {
                    ghDoc.AddObject(obj, false);
                    if (obj != group)
                    {
                        group.AddObject(obj.InstanceGuid);
                    }
                }

                if (objectsToDelete.Count > 0)
                {
                    ghDoc.UndoUtil.MergeRecords(2);
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
