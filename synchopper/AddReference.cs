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
    public class AddReference
    {
        private readonly static string _prefix = "_xref: ";

        public static void OpenImportFileDialog()
        {
            using var openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "Grasshopper Files (*.gh;*.ghx)|*.gh;*.ghx";
            openFileDialog.Title = "Select a Grasshopper File";

            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                GH_Document ghDoc = Grasshopper.Instances.ActiveCanvas.Document;
                ImportFile(ghDoc, openFileDialog.FileName, true);
            }
        }

        public static void SaveSelectionAsReference()
        {
            using var saveFileDialog = new SaveFileDialog();
            saveFileDialog.Filter = "Grasshopper Files (*.gh;*.ghx)|*.gh;*.ghx";
            saveFileDialog.Title = "Save Selection as Reference";

            if (saveFileDialog.ShowDialog() != DialogResult.OK)
            {
                return;
            }

            GH_Document ghDoc = Grasshopper.Instances.ActiveCanvas.Document;
            if (ghDoc is null)
            {
                Rhino.RhinoApp.WriteLine("Synchopper: No active document.");
                return;
            }

            var selectedObjects = ghDoc.SelectedObjects();
            if (selectedObjects.Count == 0)
            {
                Rhino.RhinoApp.WriteLine("Synchopper: No objects selected.");
                return;
            }
            
            var copyDoc = GH_Document.DuplicateDocument(ghDoc);
            copyDoc.DeselectAll();

            if (copyDoc == null)
            {
                Rhino.RhinoApp.WriteLine("Synchopper: Hmm... something went wrong when I tried to copy the existing file.");
                return;
            }

            var selectedObjectsGuids = selectedObjects
                .Select(o => o.InstanceGuid)
                .ToHashSet();

            // we need to use only components that are not already in a sync group
            var referenceGroups = copyDoc.Objects
                .Where(o => o is GH_Group && o.NickName.StartsWith(_prefix))
                .Cast<GH_Group>();

            var objectsInGroups = referenceGroups
                .SelectMany(g => g.ObjectsRecursive())
                .ToHashSet();

            var objectsNotInGroups = copyDoc.Objects
                .Except(objectsInGroups)
                .Except(referenceGroups)
                .Select(o => o.InstanceGuid)
                .ToHashSet();

            if (objectsNotInGroups.Count == 0)
            {
                Rhino.RhinoApp.WriteLine("Synchopper: This file can't be saved as reference because it doesn't contain any components that are not referenced already.");
                return;
            }

            var selectedObjectsNotInGroupsIds = objectsNotInGroups
                .Intersect(selectedObjectsGuids)
                .ToList();

            if (selectedObjectsNotInGroupsIds.Count == 0)
            {
                Rhino.RhinoApp.WriteLine("Synchopper: No objects selected that are not already referenced.");
                return;
            }

            var selectedObjectsNotInGroups = selectedObjectsNotInGroupsIds
            .Select(id => ghDoc.FindObject(id, false))
            .ToList();

            // remove selected objects from the current document
            ghDoc.UndoUtil.RecordRemoveObjectEvent("Remove selected objects", selectedObjects);
            foreach (var obj in selectedObjects)
            {
                ghDoc.RemoveObject(obj, false);
            }

            // save the current file
            var io = new GH_DocumentIO(ghDoc);
            io.Save();

            // remove not selected objects from the reference document
            var notSelectedObjectsNotInGroups = objectsNotInGroups
                .Except(selectedObjectsNotInGroupsIds)
                .Select(id => copyDoc.FindObject(id, false))
                .ToList();


            copyDoc.RemoveObjects(notSelectedObjectsNotInGroups, false);

            // Mutate ids in the reference doc
            copyDoc.DestroyProxySources();
            copyDoc.MutateAllIds();

            // import current document into the copy document
            ImportDoc(copyDoc, ghDoc, false);

            // save the copy document
            io = new GH_DocumentIO(copyDoc);
            var saveResult = io.SaveQuiet(saveFileDialog.FileName);

            if (!saveResult)
            {
                Rhino.RhinoApp.WriteLine("Synchopper: Failed to save reference.");
                
                // undo the changes to the current document
                ghDoc.Undo();
                return;
            }

            // we need to assign the name for correct import
            copyDoc.FilePath = saveFileDialog.FileName;

            // import the reference document into the current document
            var importResult = ImportDoc(ghDoc, copyDoc, true);
            if (!importResult)
            {
                Rhino.RhinoApp.WriteLine("Synchopper: Failed to import the reference document.");

                // undo the changes to the current document
                ghDoc.Undo();
                return;
            }

            Grasshopper.Instances.ActiveCanvas.Refresh();
        }

        public static void UpdateAllReferences()
        {
            GH_Document ghDoc = Grasshopper.Instances.ActiveCanvas.Document;
            if (ghDoc is null)
            {
                Rhino.RhinoApp.WriteLine("Synchopper: No active document.");
                return;
            }

            var groups = ghDoc.Objects
                .Where(o => o is GH_Group && o.NickName.StartsWith(_prefix))
                .Cast<GH_Group>()
                .ToList();

            if (groups.Count == 0)
            {
                Rhino.RhinoApp.WriteLine("Synchopper: No references to update.");
                return;
            }

            int successCount = 0;
            foreach (var group in groups)
            {
                var filePath = group.NickName.Substring(_prefix.Length);
                bool importResult = ImportFile(ghDoc, filePath, false);
                if (importResult)
                {
                    successCount++;
                }
            }

            ghDoc.NewSolution(true);
            Grasshopper.Instances.ActiveCanvas.Refresh();

            Rhino.RhinoApp.WriteLine($"Synchopper: Updated {successCount}/{groups.Count} references.");
        }

        internal static bool ImportFile(GH_Document ghDoc, string path, bool recompute)
        {
            var referenceDoc = ReadGhFile(path);
            if (referenceDoc is null)
            {
                Rhino.RhinoApp.WriteLine("Synchopper: Failed to read the file.");
                return false;
            }

            return ImportDoc(ghDoc, referenceDoc, recompute);
        }

        internal static bool ImportDoc(GH_Document ghDoc, GH_Document referenceDoc, bool recompute)
        {
            if (ghDoc is null)
            {
                Rhino.RhinoApp.WriteLine("Synchopper: No active document.");
                return false;
            }

            if (referenceDoc is null)
            {
                Rhino.RhinoApp.WriteLine("Synchopper: No reference document.");
                return false;
            }

            // after coping the FilePath property will become null
            var filePath = referenceDoc.FilePath;

            // make a copy of the reference document before mutating the guids
            referenceDoc = GH_Document.DuplicateDocument(referenceDoc);

            if (referenceDoc is null)
            {
                Rhino.RhinoApp.WriteLine("Synchopper: Hmm... something went wrong when I tried to copy the reference file.");
                return false;
            }

            // Important! Change all the instance guids of the objects in the reference document
            // so that they don't conflict with the objects in the current document
            referenceDoc.MutateAllIds();

            var objectsToDelete = new List<IGH_DocumentObject>();
            var objectsToAdd = new List<IGH_DocumentObject>();

            string undoMessage = "Add reference group";

            var groupName = _prefix + filePath;
            GH_Group group;
            var existingGroup = ghDoc.Objects.FirstOrDefault(o => o.NickName == groupName) as GH_Group;
            if (existingGroup is not null)
            {
                objectsToDelete.AddRange(existingGroup.ObjectsRecursive());
                group = existingGroup;
                undoMessage = "Update reference group";
            }
            else
            {
                group = new GH_Group()
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
                return false;
            }

            objectsToAdd.AddRange(importObjects);

            int numberOfUndos = 0;

            // remove objects from the current file
            if (objectsToDelete.Count > 0)
            {
                ghDoc.UndoUtil.RecordRemoveObjectEvent(undoMessage, objectsToDelete);
                numberOfUndos++;

                ghDoc.RemoveObjects(objectsToDelete, false);
            }

            // add objects to the current file
            ghDoc.UndoUtil.RecordAddObjectEvent(undoMessage, objectsToAdd);
            numberOfUndos++;

            foreach (var obj in objectsToAdd)
            {
                ghDoc.AddObject(obj, false);

                if (obj != group)
                {
                    // add objects to the group
                    group.AddObject(obj.InstanceGuid);
                }
            }

            // if any of the components were previously connected to parameters that do not exist in the new file,
            // it will create a "ghost" wire that is not connected to anything.
            // We need to remove those non-existent parameters.
            var importedGuids = objectsToAdd.Select(o => o.InstanceGuid);
            RemoveGhostParams(ghDoc, importedGuids);

            // merge different types of undos
            if (numberOfUndos > 1)
            {
                ghDoc.UndoUtil.MergeRecords(numberOfUndos);
            }

            if (recompute)
            {
                ghDoc.NewSolution(true);
                Grasshopper.Instances.ActiveCanvas.Refresh();
            }

            return true;
        }

        private static void RemoveGhostParams(GH_Document ghDoc, IEnumerable<Guid> importedGuids)
        {
            foreach (var guid in importedGuids)
            {
                var importedObject = ghDoc.FindObject(guid, false);

                if (importedObject is IGH_Param param)
                {
                    DisconnectRedundantWires(param, ghDoc);
                }
                else if (importedObject is IGH_Component component)
                {
                    var allParams = component.Params.Input.ToList();
                    allParams.AddRange(component.Params.Output);
                    foreach (var par in allParams)
                    {
                        DisconnectRedundantWires(par, ghDoc);
                    }
                }
            }
        }

        private static void DisconnectRedundantWires(IGH_Param param, GH_Document ghDoc)
        {
            bool changed = false;

            for(int i = param.SourceCount - 1; i >= 0; i--)
            {
                var source = param.Sources[i];
                var parentId = source.Attributes.GetTopLevel?.InstanceGuid;
                if (parentId == null || ghDoc.FindObject(parentId.Value, false) == null)
                {
                    // remove
                    param.RemoveSource(source);
                    changed = true;
                }
            }

            for (int i = param.Recipients.Count - 1; i >= 0; i--)
            {
                var recipient = param.Recipients[i];
                var parentId = recipient.Attributes.GetTopLevel?.InstanceGuid;
                if (parentId == null || ghDoc.FindObject(parentId.Value, false) == null)
                {
                    // remove
                    param.Recipients.Remove(recipient);
                    changed = true;
                }
            }

            if (changed)
            {
                param.OnAttributesChanged(); // not sure if it's needed
            }
        }

        private static GH_Document? ReadGhFile(string path)
        {
            if (!File.Exists(path))
            {
                Rhino.RhinoApp.WriteLine("Synchopper: File does not exist.");
                return null;
            }

            var io = new GH_DocumentIO();

            if (!io.Open(path))
            {
                Rhino.RhinoApp.WriteLine("Synchopper: Failed to open file.");
                return null;
            }

            var referenceDoc = io.Document;
            if (referenceDoc is null)
            {
                Rhino.RhinoApp.WriteLine("Synchopper: Failed to read the file.");
            }

            return referenceDoc;
        }
    }
}
