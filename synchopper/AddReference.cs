using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
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

            if (!io.Open(path))
            {
                Rhino.RhinoApp.WriteLine("Synchopper: Failed to open file.");
                return;
            }

            var referenceDoc = io.Document;

            if (referenceDoc is null)
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
            }

            // we don't need to import any referenced groups to avoid recursive import
            var sycnhopperGroups = referenceDoc.Objects
                .Where(o => o is GH_Group && o.NickName.StartsWith(_prefix))
                .Cast<GH_Group>()
                .ToHashSet();

            var skipObjects = sycnhopperGroups
                .SelectMany(g => g.ObjectsRecursive())
                .ToHashSet();

            skipObjects.UnionWith(sycnhopperGroups); // also skip the groups itself 

            var importObjects = referenceDoc.Objects
                .Except(skipObjects)
                .ToList();

            if (importObjects.Count == 0)
            {
                Rhino.RhinoApp.WriteLine("Synchopper: No objects to import.");
                return;
            }

            objectsToAdd.AddRange(importObjects);
            //CheckDuplicates(ghDoc, importObjects);

            int numberOfUndos = 0;
            // remove objects from the current file
            if (objectsToDelete.Count > 0)
            {
                ghDoc.UndoUtil.RecordRemoveObjectEvent("Remove existing group objects", objectsToDelete);
                numberOfUndos++;

                foreach (var item in objectsToDelete)
                {
                    ghDoc.RemoveObject(item, false);
                }

                ghDoc.DestroyObjectTable();
            }

            ghDoc.UndoUtil.RecordAddObjectEvent("Import new objects", importObjects);
            numberOfUndos++;

           
            
            referenceDoc.RemoveObjects(skipObjects, false);
            var tempGroup = new GH_Group()
            {
                NickName = Guid.NewGuid().ToString()
            };

            foreach (var item in referenceDoc.Objects)
            {
                tempGroup.AddObject(item.InstanceGuid);
            }

            referenceDoc.AddObject(tempGroup, false);
            referenceDoc.MutateAllIds();

            ghDoc.MergeDocument(referenceDoc);

            //foreach (var obj in objectsToAdd)
            //{
            //    // if there is an existing object with the same id, we need to change id of the existing object
            //    var existingObject = ghDoc.Objects.FirstOrDefault(o => o.InstanceGuid == obj.InstanceGuid);
            //    if (existingObject != null)
            //    {
            //        // TODO: add undo action for this
            //        existingObject?.NewInstanceGuid();
            //        ghDoc.DestroyObjectTable();
            //    }

            //    ghDoc.AddObject(obj, false);
            //    ghDoc.DestroyObjectTable();

            //    if (obj != group)
            //    {
            //        group.AddObject(obj.InstanceGuid);                    
            //    }
            //}

            //ghDoc.DestroyObjectTable(); // just in case

            // merge different types of undos
            if (numberOfUndos > 1)
            {
                //ghDoc.UndoUtil.MergeRecords(numberOfUndos);
            }

            ghDoc.NewSolution(true);
            Grasshopper.Instances.ActiveCanvas.Refresh();

        }

        private static bool CheckDuplicates(GH_Document ghDoc, List<IGH_DocumentObject> importObjects)
        {
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

            var existingObjectsChangeId = existingObjectsOutsideSyncGroups
                .Select(o => o.InstanceGuid).ToList()
                .Intersect(importObjects.Select(o => o.InstanceGuid).ToList())
                .ToList();

            if (existingObjectsChangeId.Count > 0)
            {
                var message = $"Synchopper: There are {existingObjectsChangeId.Count} components in the current file that share the same ID with the objects from the imported file. " +
                    $"You don't need to do anything, the existing components will be given new IDs.\n\n" +
                    $"Do you want to proceed?";

                if (System.Windows.Forms.MessageBox.Show(message, "Synchopper", System.Windows.Forms.MessageBoxButtons.OKCancel) == System.Windows.Forms.DialogResult.Cancel)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
