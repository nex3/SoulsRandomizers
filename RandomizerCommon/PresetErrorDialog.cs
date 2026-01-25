using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using YamlDotNet.Core;

namespace RandomizerCommon
{
    public partial class PresetErrorDialog : Form
    {
        public PresetErrorDialog(string yaml, YamlException ex)
        {
            InitializeComponent();
            errorMessage.Text = ex.Message;
            this.yaml.Text = yaml;
            this.yaml.Select(ex.Start.Index, ex.End.Index - ex.Start.Index);
            this.yaml.SelectionBackColor = System.Drawing.Color.FromArgb(30, System.Drawing.Color.Red);
            this.yaml.ScrollToCaret();
            this.yaml.DeselectAll();
        }

        private void PresetErrorDialog_Load(object sender, EventArgs e)
        {

        }
    }
}
