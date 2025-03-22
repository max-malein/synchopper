using Grasshopper.GUI;
using Grasshopper.Kernel;
using System.Windows.Forms;

namespace synchopper
{
    public class SynchopperMenu: GH_AssemblyPriority
    {
        public override GH_LoadingInstruction PriorityLoad()
        {
            Grasshopper.Instances.CanvasCreated += RegisterNewMenuItems;

            return GH_LoadingInstruction.Proceed;
        }

        private void RegisterNewMenuItems(Grasshopper.GUI.Canvas.GH_Canvas canvas)
        {
            Grasshopper.Instances.CanvasCreated -= RegisterNewMenuItems;
            GH_DocumentEditor docEditor = Grasshopper.Instances.DocumentEditor;

            if (docEditor != null)
                SetupOmrtMenu(docEditor);
        }

        private void SetupOmrtMenu(GH_DocumentEditor docEditor)
        {
            ToolStripMenuItem? synchopperTab = null;

            for (int i = 0; i < docEditor.MainMenuStrip.Items.Count; i++)
            {
                if (docEditor.MainMenuStrip.Items[i].Name == "Synchopper Tab")
                {
                    synchopperTab = (ToolStripMenuItem)docEditor.MainMenuStrip.Items[i];
                    break;
                }
            }

            synchopperTab ??= new()
            {
                Name = "Synchopper Tab",
                Text = "Synchopper"
            };

            docEditor.MainMenuStrip.SuspendLayout();
            docEditor.MainMenuStrip.Items.Add(synchopperTab);

            var addReference = new ToolStripMenuItem
            {
                Text = "Add reference...",
                ToolTipText = "Add reference to an existing grasshopper file.",
            };

            addReference.Click += AddReference_Click;
            synchopperTab.DropDownItems.Add(addReference);

            var updateAll = new ToolStripMenuItem
            {
                Text = "Update all",
                ToolTipText = "Update all references."
            };

            updateAll.Click += UpdateAll_Click;
            synchopperTab.DropDownItems.Add(updateAll);

            docEditor.MainMenuStrip.ResumeLayout(false);
            docEditor.MainMenuStrip.PerformLayout();
        }

        private void UpdateAll_Click(object sender, System.EventArgs e)
        {
            throw new System.NotImplementedException();
        }

        private void AddReference_Click(object sender, System.EventArgs e)
        {
            throw new System.NotImplementedException();
        }
    }
}
