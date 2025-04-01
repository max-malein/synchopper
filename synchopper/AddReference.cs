using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace synchopper
{
    public class AddReference
    {
        private readonly static string _prefix = "_synchopper: ";

        public static void OpenImportFileDialog()
        {
            using var openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "Grasshopper Files (*.gh;*.ghx)|*.gh;*.ghx";
            openFileDialog.Title = "Select a Grasshopper File";

            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {                
                ImportFile(openFileDialog.FileName, true);
            }
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
                bool importResult = ImportFile(filePath, false);
                if (importResult)
                {
                    successCount++;
                }
            }

            ghDoc.NewSolution(true);
            Grasshopper.Instances.ActiveCanvas.Refresh();

            Rhino.RhinoApp.WriteLine($"Synchopper: Updated {successCount}/{groups.Count} references.");
        }

        internal static bool ImportFile(string path, bool recompute)
        {            
            var referenceDoc = ReadGhFile(path);
            if (referenceDoc is null)
            {
                return false;
            }

            GH_Document ghDoc = Grasshopper.Instances.ActiveCanvas.Document;
            if (ghDoc is null)
            {
                Rhino.RhinoApp.WriteLine("Synchopper: No active document.");
                return false;
            }

            var objectsToDelete = new List<IGH_DocumentObject>();
            var objectsToAdd = new List<IGH_DocumentObject>();

            string undoMessage = "Add reference group";

            var groupName = _prefix + path;
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
                group = existingGroup ?? new GH_Group()
                {
                    NickName = groupName,
                    Colour = Color.FromArgb(100, Color.Pink),
                };

                objectsToAdd.Add(group);
            }

            // Important! Change all the instance guids of the objects in the reference document
            // so that they don't conflict with the objects in the current document
            referenceDoc.MutateAllIds();

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

            ghDoc.UndoUtil.RecordAddObjectEvent(undoMessage, importObjects);
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
