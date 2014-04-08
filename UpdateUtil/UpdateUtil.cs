using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

/*
 * CONTENIDO DEL ARCHIVO DE ACTUALIZACIÓN
 * 
UPDATE_DEFINITION|14.1.1|2014-04-03 9:00am|URGENT-NORMAL-CRITICAL|Descripcion
INSTALL_FROM_INTERNET|url|fileName|Descripcion|RunParams
INSTALL_FROM_INTERNET|http://download.microsoft.com/download/9/5/A/95A9616B-7A37-4AF6-BC36-D6EA96C8DAAE/dotNetFx40_Full_x86_x64.exe | netfx4full.exe | .NET v4 | /norestart /passive
DownloadFileName.ext|CopyToDestinationFileName.ext|{app} or fullPath|EXE|Description|RunParams
null|FileName.ext|{app}|FileToDelete|Description|null
null|null|{app}/DirectoryName|FolderToDelete|Description|null
HKCU|null|null|RegKey|Description|Create/Delete_RegKey/Delete_Value;Key;Value
DownloadFileName.msi|CopyToDestinationFileName.msi|{tmp}|MSI|Description|/i ""{targetFileName}"" /quiet
*/

namespace DM.Core.Util
{
    public class UpdateUtil : IDisposable
    {
        // Private constants
        const string DEF_STARTUP_FILENAME = "Update.txt";
        const string DEF_DOWNLOAD_DIRNAME = "Update.tmp";
        const string DEF_DOWNLOAD_URI = "http://dev-mexico.com/apps/updates/";



        // Private Properties
        private string StartUpFilePath { get; set; }
        private string DownloadDirectoryPath { get; set; }
        private DirectoryInfo DownloadDirectory { get; set; }
        private string UpdateFileContent { get; set; }
        private List<string> UpdateFileLines { get; set; }
        private List<FileDefinition> UpdateFileDefinition { get; set; }
        private List<ThirdPartyInstallFile> ThirdPartyInstalls { get; set; }
        private bool IsDownloading { get; set; }
        private List<string> DownloadErrors { get; set; }

        private DirectoryInfo diExe;

        // Public Properties
        public string Version { get; set; }
        public DateTime ReleaseDate { get; set; }
        public E_UpdatePriority UpdatePriority { get; set; }
        public string ReleaseDescription { get; set; }

        public string DownloadUriRoot { get; set; }

        #region Delegados

        public delegate void DownloadStartEventHandler(object sender, FileDefinition e);
        public delegate void DownloadThirdPartyInstallStartEventHandler(object sender, ThirdPartyInstallFile e);
        public delegate void SendMenssageEventHandler(object sender, string Message);

        #endregion

        #region Eventos

        public event DownloadStartEventHandler DownloadStart;
        private void OnDownloadStart(FileDefinition e)
        {
            if (DownloadStart != null)
                DownloadStart(this, e);
        }

        public event DownloadThirdPartyInstallStartEventHandler DownloadThirdPartyInstallStart;
        private void OnDownloadThirdPartyInstallStart(ThirdPartyInstallFile e)
        {
            if (DownloadThirdPartyInstallStart != null)
                DownloadThirdPartyInstallStart(this, e);
        }

        public event EventHandler DownloadEnds;
        private void OnDownloadEnds(EventArgs e)
        {
            if (DownloadEnds != null)
                DownloadEnds(this, e);
        }


        public event DownloadProgressChangedEventHandler DownloadProgress;
        private void OnDownloadProgress(DownloadProgressChangedEventArgs e)
        {
            if (DownloadProgress != null)
                DownloadProgress(this, e);
        }

        public event SendMenssageEventHandler SendMessage;
        private void OnSendMessage(string message)
        {
            if (SendMessage != null)
                SendMessage(this, message);
        }

        public event EventHandler UpdateReleased;
        private void OnUpdateReleased(EventArgs e)
        {
            if (UpdateReleased != null)
                UpdateReleased(this, e);
        }

        public event EventHandler UpdateInfoDownloaded;
        private void OnUpdateInfoDownloaded(EventArgs e)
        {
            if (UpdateInfoDownloaded != null)
                UpdateInfoDownloaded(this, e);
        }

        public event EventHandler UpdateFinished;
        private void OnUpdateFinished(EventArgs e)
        {
            if (UpdateFinished != null)
                UpdateFinished(this, e);
        }

        #endregion

        public UpdateUtil()
        {
            diExe = new DirectoryInfo(Application.ExecutablePath);

            
            DownloadDirectoryPath = Path.Combine(diExe.Parent.FullName, DEF_DOWNLOAD_DIRNAME);
            StartUpFilePath = Path.Combine(DownloadDirectoryPath, DEF_STARTUP_FILENAME);

            DownloadDirectory = new DirectoryInfo(DownloadDirectoryPath);

            if (!DownloadDirectory.Exists)
                DownloadDirectory.Create();


            UpdateFileContent = string.Empty;
            UpdateFileLines = new List<string>();
            UpdateFileDefinition = new List<FileDefinition>();
            ThirdPartyInstalls = new List<ThirdPartyInstallFile>();
            IsDownloading = false;
            DownloadErrors = new List<string>();

            Version = string.Empty;
            ReleaseDate = DateTime.Today.AddYears(-1);
            UpdatePriority = E_UpdatePriority.Normal;
            ReleaseDescription = string.Empty;

            DownloadUriRoot = DEF_DOWNLOAD_URI;
        }

        #region GetUpdateInfoFile
        /// <summary>
        /// Descargar el archivo de actualización
        /// </summary>
        public void GetUpdateInfoFile()
        {
            this.OnSendMessage("Downloading update info file...");

            using (WebClient wcUpdate = new WebClient())
            {
                // fix uri slash...
                if (!this.DownloadUriRoot.EndsWith("/"))
                    this.DownloadUriRoot = string.Concat(this.DownloadUriRoot, "/");


                string uriUpdateFile = string.Concat(DownloadUriRoot, DEF_STARTUP_FILENAME);
                // Si hay archivos descargados, realizar limpieza.
                if (ExistUpdateInfoFile())
                    this.CleanUp();

                // Descargar el archivo de definición.
                wcUpdate.CachePolicy = new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore);
                wcUpdate.DownloadFileCompleted += wcUpdate_DownloadFileCompleted;

                IsDownloading = true;
                wcUpdate.DownloadFileAsync(new Uri(uriUpdateFile), StartUpFilePath);

                while (IsDownloading)
                    Application.DoEvents();
            }

            this.ReadUpdateInfoFile();
        }

        void wcUpdate_DownloadFileCompleted(object sender, System.ComponentModel.AsyncCompletedEventArgs e)
        {
            IsDownloading = false;
            this.OnUpdateInfoDownloaded(e);
        }
        #endregion


        public void RunUpdate()
        {
            try
            {
                Application.UseWaitCursor = true;


                #region Descargar la lista de archivos...
                DownloadErrors = new List<string>();

                // Archivos y acciones...
                foreach (FileDefinition fd in UpdateFileDefinition)
                {
                    // Tipos que no son archivos... no se pueden descargar... Se omiten.
                    if (fd.FileType == E_FileType.RegKey
                        || fd.FileType == E_FileType.FileToDelete
                        || fd.FileType == E_FileType.FolderToDelete)
                        continue;

                    // fix uri slash...
                    if (!this.DownloadUriRoot.EndsWith("/"))
                        this.DownloadUriRoot = string.Concat(this.DownloadUriRoot, "/");

                    try
                    {
                        DownloadUpdateData(fd);
                    }
                    catch (Exception ex)
                    {
                        DownloadErrors.Add(string.Format("Error al descargar el archivo '{0}'. Mensaje: {1}", fd.SourceFileName, ex.Message));
                        continue;
                    }
                }

                // Third Party Installers...
                foreach (ThirdPartyInstallFile tpf in ThirdPartyInstalls)
                {
                    try { DownloadThirdPartyInstallers(tpf); }
                    catch (Exception ex)
                    {
                        DownloadErrors.Add(string.Format("Error al descargar el instalador '{0}'. Mensaje: {1}", tpf.Uri, ex.Message));
                        continue;
                    }
                }
                #endregion

                //
                // Procesar las operaciones definidas...
                //
                #region Paso 1: Matar procesos...
                foreach (var fd in UpdateFileDefinition)
                {
                    if (fd.FileType == E_FileType.EXE)
                    {
                        List<System.Diagnostics.Process> procs = new List<System.Diagnostics.Process>();
                        foreach (var proc in System.Diagnostics.Process.GetProcesses())
                        {
                            if (proc.ProcessName.Contains(fd.TargetFileName))
                                procs.Add(proc);
                        }

                        foreach (var p in procs)
                            p.Kill();
                    }
                }
                #endregion

                #region Paso 2: Procesar llaves de registros y su operación (RunParams)
                foreach (var fd in UpdateFileDefinition)
                {
                    if (fd.FileType == E_FileType.RegKey)
                    {
                        ProcessRegistryKeyOption(fd.SourceFileName, fd.RunParams);
                    }
                }
                #endregion

                #region Paso 3: Eliminar archivos...
                foreach (var fd in UpdateFileDefinition)
                {
                    if (fd.FileType == E_FileType.FileToDelete)
                    {
                        FileInfo fi = new FileInfo(Path.Combine(fd.TargetDirectoryPath, fd.TargetFileName));
                        if (fi.Exists)
                            fi.Delete();
                    }
                }
                #endregion

                #region Paso 4: Eliminar directorios...
                foreach (var fd in UpdateFileDefinition)
                {
                    if (fd.FileType == E_FileType.FolderToDelete)
                    {
                        if (fd.TargetDirectory.Exists)
                        {
                            // Antes de eliminar el directorio, hay que eliminar su contenido...
                            foreach (var d in fd.TargetDirectory.GetDirectories())
                                DeleteDirectoryContent(d);

                            // Eliminar los archivos restantes del directorio base..
                            foreach (var f in fd.TargetDirectory.GetFiles())
                                f.Delete();

                            fd.TargetDirectory.Delete();
                        }
                    }
                }
                #endregion

                #region Paso 5: Instalar paquetes de instalación de terceros (NetFx, Crystal Runtime, etc)
                foreach (var tpf in ThirdPartyInstalls)
                {
                    if (File.Exists(tpf.TargetFilename))
                    {
                        this.OnSendMessage(string.Format("Instalando {0}...", tpf.Description));
                        System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo();
                        if (tpf.TargetFilename.EndsWith(".msi"))
                        {
                            string runParams = tpf.RunParams.Replace("{targetFileName}", tpf.TargetFilename);
                            psi.FileName = "msiexec.exe";
                            psi.Arguments = runParams;
                            psi.UseShellExecute = true;
                            psi.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;

                            System.Diagnostics.Process proc = System.Diagnostics.Process.Start(psi);
                            proc.WaitForExit();

                        }
                        else
                        {
                            psi.FileName = tpf.TargetFilename;
                            psi.Arguments = tpf.RunParams;
                            psi.UseShellExecute = true;
                            
                            System.Diagnostics.Process proc = System.Diagnostics.Process.Start(psi);
                            proc.WaitForExit();
                        }
                    }
                }
                #endregion

                #region Paso 6: Ejecutar archivos de instalación (MSI o EXE con params)
                foreach (FileDefinition fd in UpdateFileDefinition)
                {
                    string runParams = fd.RunParams.Trim();

                    // fix tag 'targetFileName'...
                    runParams = runParams.Replace("{targetFileName}", Path.Combine(this.DownloadDirectoryPath, fd.SourceFileName));

                    // Tipos que no son archivos... no se pueden descargar... Se omiten.
                    if (fd.FileType == E_FileType.MSI || fd.FileType == E_FileType.EXE)
                    {
                        // Solo los EXE con parámetros de ejecución se pueden correr...
                        if (fd.FileType == E_FileType.EXE && fd.RunParams.ToLower().Equals("null"))
                            continue;

                        this.OnSendMessage(string.Format("Instalando {0}...", fd.Description));
                        System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo();
                        if (fd.FileType == E_FileType.MSI)
                        {
                            psi.FileName = "msiexec.exe";
                            psi.Arguments = runParams;
                            psi.UseShellExecute = true;
                            
                            System.Diagnostics.Process proc = System.Diagnostics.Process.Start(psi);
                            proc.WaitForExit();

                        }
                        else
                        {
                            psi.FileName = fd.TargetFileName;
                            psi.Arguments = runParams;
                            psi.UseShellExecute = true;
                            
                            System.Diagnostics.Process proc = System.Diagnostics.Process.Start(psi);
                            proc.WaitForExit();
                        }
                    }

                }
                #endregion


                OnUpdateFinished(null);
            }
            finally
            {
                Application.UseWaitCursor = false;
                this.CleanUp();
            }
        }




        public void Dispose()
        {
            this.CleanUp();
        }

        //
        // PRIVATE METHODS
        //


        #region ExistUpdateInfoFile
        /// <summary>
        /// Validar si existe el archivo
        /// </summary>
        /// <returns></returns>
        private bool ExistUpdateInfoFile()
        {
            return File.Exists(StartUpFilePath);
        }
        #endregion

        #region ReadUpdateInfoFile
        /// <summary>
        /// Leer el contenido del archivo de actualización.
        /// </summary>
        private void ReadUpdateInfoFile()
        {
            this.OnSendMessage("Loading update info file...");

            UpdateFileContent = string.Empty;

            // Leer contenido...
            using (StreamReader sr = new StreamReader(StartUpFilePath))
            {
                UpdateFileContent = sr.ReadToEnd();
                sr.Close();
            }

            // Cargar lineas de definición del archivo...
            string[] lines = UpdateFileContent.Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string line in lines)
                UpdateFileLines.Add(line.Replace(Environment.NewLine, string.Empty));

            // Cargar la info en las propiedades...
            this.OnSendMessage("Loading definitions file...");

            if (UpdateFileLines.Count <= 0)
                throw new Exception("Invalid update file.");

            UpdateFileDefinition = new List<FileDefinition>();
            ThirdPartyInstalls = new List<ThirdPartyInstallFile>();

            foreach (string info in this.UpdateFileLines)
            {
                // Ignorar lineas vacías.
                if (info.Length <= 0)
                    continue;
                // Dividir parámetros de la línea de definición.
                string[] infoData = info.Split(new char[] { '|' });

                // Definición de la actualización (Version, Fecha, Descripcion)...
                if (info.StartsWith("UPDATE_DEFINITION"))
                {
                    this.OnSendMessage("Reading version info...");

                    Version = string.Empty;
                    ReleaseDate = DateTime.Today.AddYears(-1);
                    ReleaseDescription = string.Empty;

                    try
                    {
                        // infoData[0] = "UPDATE_DEFINITION";
                        this.Version = infoData[1];
                        this.ReleaseDate = DateTime.Parse(infoData[2]);
                        this.UpdatePriority = ParseUpdatePriority(infoData[3]);
                        this.ReleaseDescription = infoData[4];
                    }
                    catch { throw new Exception("Invalid update data definition."); }
                }
                else if (info.StartsWith("INSTALL_FROM_INTERNET"))
                {
                    // Archivos de instalación de terceros, para descargar de Internet.
                    ThirdPartyInstallFile tif = new ThirdPartyInstallFile(
                        infoData[1] /*uri*/,
                        infoData[2] /*fileName*/,
                        infoData[3] /*descripcion*/,
                        infoData[4] /*runParams*/);
                    ThirdPartyInstalls.Add(tif);
                }
                else
                {
                    FileDefinition fd = new FileDefinition(
                        infoData[0] /*SourceFile*/,
                        infoData[1] /*TargetFile*/,
                        infoData[2] /*TargetDirectory*/,
                        infoData[3] /*FileType*/,
                        infoData[4] /*Description*/,
                        infoData[5] /*RunParams*/);

                    // Agregar la definición a la lista.
                    UpdateFileDefinition.Add(fd);
                }
            }
        }
        #endregion


        #region Descarga de Archivos

        private void DownloadUpdateData(FileDefinition fd)
        {
            this.OnSendMessage(string.Format("Descargando {0}...", fd.Description));

            using (WebClient wcDownloadData = new WebClient())
            {
                wcDownloadData.CachePolicy = new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore);
                wcDownloadData.DownloadProgressChanged += wcDownloadData_DownloadProgressChanged;
                wcDownloadData.DownloadFileCompleted += wcDownloadData_DownloadFileCompleted;


                Uri fileToDownload = new Uri(string.Concat(DownloadUriRoot, fd.SourceFileName));
                string downloadTo = Path.Combine(DownloadDirectoryPath, fd.TargetFileName);

                IsDownloading = true;
                this.OnDownloadStart(fd);
                wcDownloadData.DownloadFileAsync(fileToDownload, downloadTo);

                while (IsDownloading)
                    Application.DoEvents();
            }
        }

        private void DownloadThirdPartyInstallers(ThirdPartyInstallFile tpf)
        {
            this.OnSendMessage(string.Format("Descargando {0}...", tpf.Description));

            using (WebClient wcDownloadData = new WebClient())
            {
                wcDownloadData.CachePolicy = new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore);
                wcDownloadData.DownloadProgressChanged += wcDownloadData_DownloadProgressChanged;
                wcDownloadData.DownloadFileCompleted += wcDownloadData_DownloadFileCompleted;

                IsDownloading = true;
                this.OnDownloadThirdPartyInstallStart(tpf);
                wcDownloadData.DownloadFileAsync(new Uri(tpf.Uri), tpf.TargetFilename);

                while (IsDownloading)
                    Application.DoEvents();

                
            }
        }

        void wcDownloadData_DownloadProgressChanged(object sender, DownloadProgressChangedEventArgs e)
        {
            this.OnDownloadProgress(e);
        }

        void wcDownloadData_DownloadFileCompleted(object sender, System.ComponentModel.AsyncCompletedEventArgs e)
        {
            IsDownloading = false;

            this.OnDownloadEnds(e);
        }

        #endregion

        #region ParseUpdatePriority
        private E_UpdatePriority ParseUpdatePriority(string value)
        {
            switch (value.Trim().ToUpper())
            {
                case "CRITICAL": return E_UpdatePriority.Critical;
                case "URGENT": return E_UpdatePriority.Urgent;
                default: return E_UpdatePriority.Normal;
            }
        }
        #endregion

        #region CleanUp
        /// <summary>
        /// Limpiar archivos descargados..
        /// </summary>
        private void CleanUp()
        {
            this.OnSendMessage("Cleaning...");

            try
            {
                foreach (FileInfo fi in DownloadDirectory.GetFiles())
                {
                    try { fi.Delete(); }
                    catch { continue; }
                }

                foreach (var tpf in ThirdPartyInstalls)
                {
                    try
                    {
                        FileInfo fi = new FileInfo(tpf.TargetFilename);
                        if (fi.Exists)
                            fi.Delete();
                    }
                    catch { continue; }
                }

                try
                {
                    if (File.Exists(StartUpFilePath))
                        File.Delete(StartUpFilePath);
                }
                catch (Exception ex) 
                {
                    this.OnSendMessage(string.Concat("<ERROR>: ", ex.Message));
                    throw new Exception("Update #fail.", ex); 
                }

            }
            finally
            {
                this.OnSendMessage("");
            }
        }
        #endregion

        #region ProcessRegistryKeyOption
        private void ProcessRegistryKeyOption(string RegKey, string Options)
        {
            string[] regParts = RegKey.Split(new char[] { '\\' });
            string[] OptionValues = Options.Split(new char[] { ';' });

            #region Obtener el Root del registro...

            RegistryKey rootKey = null;
            if (regParts[0].Equals("HKLM"))
                rootKey = Registry.LocalMachine;
            else if (regParts[0].Equals("HKCU"))
                rootKey = Registry.CurrentUser;
            else if (regParts[0].Equals("HKCR"))
                rootKey = Registry.ClassesRoot;
            else if (regParts[0].Equals("HKU"))
                rootKey = Registry.Users;
            else if (regParts[0].Equals("HKCC"))
                rootKey = Registry.CurrentConfig;

            if (rootKey == null)
                throw new Exception("Invalid Registry Key.");

            #endregion

            // Establecer la entrada de registro...
            string regKeyPath = RegKey.Replace(string.Concat(regParts[0], "\\"), string.Empty);

            // Crear nueva clave de registro...
            if (OptionValues[0].ToUpper().Trim().Equals("CREATE"))
            {
                RegistryKey newKey = rootKey.CreateSubKey(regKeyPath, RegistryKeyPermissionCheck.ReadWriteSubTree);
                newKey.SetValue(OptionValues[1].Trim(), OptionValues[2].Trim());
            }
            else if (OptionValues[0].ToUpper().Trim().Equals("DELETE_REGKEY"))
            {
                rootKey.DeleteSubKey(regKeyPath, false);
            }
            else if (OptionValues[0].ToUpper().Trim().Equals("DELETE_VALUE"))
            {
                RegistryKey theKey = rootKey.OpenSubKey(regKeyPath, RegistryKeyPermissionCheck.ReadSubTree, System.Security.AccessControl.RegistryRights.ReadKey);
                theKey.DeleteValue(OptionValues[0].Trim());
            }

        }
        #endregion

        #region DeleteDirectoryContent
        /// <summary>
        /// Eliminar archivos y directorios
        /// </summary>
        /// <param name="di"></param>
        private void DeleteDirectoryContent(DirectoryInfo di)
        {
            if (di.GetDirectories().Length > 0)
            {
                foreach (var d in di.GetDirectories())
                    DeleteDirectoryContent(d);
            }
            else
            {
                foreach (var f in di.GetFiles())
                    f.Delete();
            }
        }
        #endregion

    }

    public enum E_UpdatePriority
    {
        Normal, Urgent, Critical
    }

    public enum E_FileType
    {
        MSI, EXE, TXT, DLL, Assembly, ActiveX, XML, RegKey, Unknown, FileToDelete, FolderToDelete
    }

    public class FileDefinition
    {

        public string SourceFileName { get; set; }
        public string TargetFileName { get; set; }
        public string TargetDirectoryPath { get; set; }
        public DirectoryInfo TargetDirectory { get; set; }
        public E_FileType FileType { get; set; }
        public string RunParams { get; set; }
        public string Description { get; set; }


        private DirectoryInfo diExe = null;

        public FileDefinition(string sourceFileName, string targetFileName, string targetDirectoryPath, string fileType, string description, string runParams = "")
        {
            diExe = new DirectoryInfo(Application.ExecutablePath);

            this.SourceFileName = sourceFileName.Trim();
            this.TargetFileName = targetFileName.Trim();
            this.TargetDirectoryPath = ProcessTargetDirectory(targetDirectoryPath.Trim());
            this.TargetDirectory = new DirectoryInfo(TargetDirectoryPath);
            this.FileType = ProcessFileType(fileType);
            this.Description = description.Trim();
            this.RunParams = runParams.Trim();

            

        }

        private string ProcessTargetDirectory(string value)
        {
            if (value.StartsWith("{"))
            {
                string tag = value.Substring(0, value.IndexOf("}"));
                string tagValue = ProcessTag(tag);
                return value.Replace(tag, tagValue);
            }
            else if (value.Contains(@":\"))
                return value;
            
            return Path.Combine(diExe.FullName, value);
        }

        private string ProcessTag(string tag)
        {
            // Procesar etiquetas...
            switch (tag)
            {
                case "{app}":

                    return diExe.Parent.FullName;
                case "{appData}":
                    return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                case "{desktop}":
                    return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                case "{docs}":
                    return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                case "{pf}":
                    return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                case "{user}":
                    return Environment.GetFolderPath(Environment.SpecialFolder.Personal);
                case "{tmp}":
                    return Path.GetTempPath();
                default:
                    return diExe.Parent.FullName;
            }
        }

        private E_FileType ProcessFileType(string type)
        {
            switch (type.ToUpper().Trim())
            {
                case "MSI": return E_FileType.MSI;
                case "EXE": return E_FileType.EXE;
                case "TXT": return E_FileType.TXT;
                case "DLL": return E_FileType.DLL;
                case "ASSEMBLY": return E_FileType.Assembly;
                case "ACTIVEX": return E_FileType.ActiveX;
                case "XML": return E_FileType.XML;
                case "REGKEY": return E_FileType.RegKey;
                case "FILETODELETE": return E_FileType.FileToDelete;
                case "FOLDERTODELETE": return E_FileType.FolderToDelete;
                default: return E_FileType.Unknown;
            }
        }
    }


    public class ThirdPartyInstallFile
    {
        public string Uri { get; set; }
        public string TargetFilename { get; set; }
        public string RunParams { get; set; }
        public string Description { get; set; }


        public ThirdPartyInstallFile(string uri, string targetFilename, string description, string runParams = "")
        {
            Uri = uri.Trim();
            TargetFilename = Path.Combine(Path.GetTempPath(), targetFilename.Trim());
            RunParams = runParams.Trim();
            Description = description.Trim();
        }
    }
}