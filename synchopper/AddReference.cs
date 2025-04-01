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
                return;
            }

            objectsToAdd.AddRange(importObjects);

            int numberOfUndos = 0;
            // remove objects from the current file
            if (objectsToDelete.Count > 0)
            {
                ghDoc.UndoUtil.RecordRemoveObjectEvent("Remove existing group objects", objectsToDelete);
                numberOfUndos++;

                ghDoc.RemoveObjects(objectsToDelete, false);
            }

            ghDoc.UndoUtil.RecordAddObjectEvent("Import new objects", importObjects);
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

            ghDoc.NewSolution(true);
            Grasshopper.Instances.ActiveCanvas.Refresh();

        }
    }
}
