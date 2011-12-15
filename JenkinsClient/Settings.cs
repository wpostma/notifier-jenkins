using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace HudsonClient
{
    public partial class Settings : Form
    {
        public Settings()
        {
            InitializeComponent();
        }

        private void saveCloseToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Properties.Settings.Default.Save();
            AdvancedSettings.Default.Save();
            Close();
        }

        private void Settings_Load(object sender, EventArgs e)
        {
            Properties.Settings.Default.Reload();
            AdvancedSettings.Default.Reload();
            propertyGridUser.SelectedObject = Properties.Settings.Default;
            propertyGridAdvanced.SelectedObject = AdvancedSettings.Default;
        }

        private void closeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void tabControl_SelectedIndexChanged(object sender, EventArgs e)
        {
        }
    }
}
