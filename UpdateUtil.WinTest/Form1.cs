using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RunUpdateTest
{
    public partial class Form1 : Form
    {
        private DM.Core.Util.UpdateUtil upt = new DM.Core.Util.UpdateUtil();

        public Form1()
        {
            InitializeComponent();

            upt.DownloadStart += upt_DownloadStart;
            upt.DownloadThirdPartyInstallStart += upt_DownloadThirdPartyInstallStart;
            upt.DownloadEnds += upt_DownloadEnds;
            upt.DownloadProgress += upt_DownloadProgress;
            upt.UpdateInfoDownloaded += upt_UpdateInfoDownloaded;
            upt.UpdateReleased += upt_UpdateReleased;
            upt.SendMessage += upt_SendMessage;
            upt.UpdateFinished += upt_UpdateFinished;

        }

        protected override void OnClosing(CancelEventArgs e)
        {
            upt.Dispose();
            base.OnClosing(e);
        }


        #region Eventos para la clase de actualización


        void upt_UpdateFinished(object sender, EventArgs e)
        {
            lblStatus.Text = "";
            lblMessage.Text = "";
            MessageBox.Show("Actualización completada.");
            Application.Exit();
        }


        void upt_SendMessage(object sender, string Message)
        {
            lblMessage.Text = Message;
        }

        void upt_UpdateReleased(object sender, EventArgs e)
        {
            lblStatus.Text = "Actualización disponible!";
        }

        void upt_UpdateInfoDownloaded(object sender, EventArgs e)
        {
            lblStatus.Text = "Archivo de actualización descargado.";
        }

        void upt_DownloadProgress(object sender, System.Net.DownloadProgressChangedEventArgs e)
        {
            pbrDownload.Value = e.ProgressPercentage;
        }

        void upt_DownloadEnds(object sender, EventArgs e)
        {
            lblStatus.Text = string.Empty;
            pbrDownload.Value = 0;
        }

        void upt_DownloadThirdPartyInstallStart(object sender, DM.Core.Util.ThirdPartyInstallFile e)
        {
            lblStatus.Text = string.Format("Descargando {0}...", e.Description);
        }

        void upt_DownloadStart(object sender, DM.Core.Util.FileDefinition e)
        {
            lblStatus.Text = string.Format("Descargando {0}...", e.Description);
        }

        #endregion


        private void cmdIniciar_Click(object sender, EventArgs e)
        {
            try
            {
                this.Cursor = Cursors.WaitCursor;

                upt.GetUpdateInfoFile();
                upt.RunUpdate();
            }
            finally
            {
                this.Cursor = Cursors.Default;
            }
            
        }

    }
}
