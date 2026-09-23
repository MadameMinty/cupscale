using Cupscale.IO;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Cupscale.Forms
{
    /// <summary> Checkbox tree of all models; checking a folder checks everything below it. </summary>
    public partial class ModelMultiSelectForm : Form
    {
        public List<string> selectedModels = new List<string>();
        readonly HashSet<string> preselected;
        bool cascading;

        public ModelMultiSelectForm(IEnumerable<string> selected)
        {
            InitializeComponent();
            preselected = new HashSet<string>(selected, StringComparer.OrdinalIgnoreCase);
        }

        private void ModelMultiSelectForm_Load(object sender, EventArgs e)
        {
            string modelDir = Config.Get("modelPath");

            if (!Directory.Exists(modelDir))
            {
                Program.ShowMessage("The saved model directory does not exist - Make sure you've set a models folder!");
                Close();
                return;
            }

            ModelSelectForm.BuildTree(new DirectoryInfo(modelDir), modelTree.Nodes);

            cascading = true;
            foreach (TreeNode node in AllNodes(modelTree.Nodes).Where(n => preselected.Contains(n.Name)))
                node.Checked = true;
            cascading = false;

            modelTree.ExpandAll();
        }

        static IEnumerable<TreeNode> AllNodes(TreeNodeCollection nodes)
        {
            foreach (TreeNode node in nodes)
            {
                yield return node;

                foreach (TreeNode child in AllNodes(node.Nodes))
                    yield return child;
            }
        }

        static bool IsModel(TreeNode node)
        {
            return node.Nodes.Count == 0 && (node.Name.EndsWith(".pth", StringComparison.OrdinalIgnoreCase) || node.Name.EndsWith(".ncnn", StringComparison.OrdinalIgnoreCase));
        }

        private void modelTree_AfterCheck(object sender, TreeViewEventArgs e)
        {
            if (cascading)
                return;

            cascading = true;
            foreach (TreeNode child in AllNodes(e.Node.Nodes))
                child.Checked = e.Node.Checked;
            cascading = false;
        }

        void SetAll(bool state)
        {
            cascading = true;
            foreach (TreeNode node in AllNodes(modelTree.Nodes))
                node.Checked = state;
            cascading = false;
        }

        private void allBtn_Click(object sender, EventArgs e)
        {
            SetAll(true);
        }

        private void noneBtn_Click(object sender, EventArgs e)
        {
            SetAll(false);
        }

        private void confirmBtn_Click(object sender, EventArgs e)
        {
            selectedModels = AllNodes(modelTree.Nodes).Where(n => n.Checked && IsModel(n)).Select(n => n.Name).ToList();
            DialogResult = DialogResult.OK;
            Close();
        }

        private void cancelBtn_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void ModelMultiSelectForm_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == (char)Keys.Enter)
            {
                e.Handled = true;
                confirmBtn_Click(null, null);
            }

            if (e.KeyChar == (char)Keys.Escape)
            {
                e.Handled = true;
                Close();
            }
        }
    }
}
