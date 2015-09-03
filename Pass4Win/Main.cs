﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Threading;
using System.IO;
using System.Security.Cryptography;
using GpgApi;
using LibGit2Sharp;
using System.Net.Sockets;

namespace Pass4Win
{
    public partial class frmMain : Form
    {
        public frmMain()
        {
            InitializeComponent();
            // Checking for appsettings
            EnableTray = false;

            // Do we have a valid password store
            if (Properties.Settings.Default.PassDirectory == "firstrun")
            {
                frmConfig Config = new frmConfig();
                var dialogResult = Config.ShowDialog();
            }
                
            //checking git status
            if (!Repository.IsValid(Properties.Settings.Default.PassDirectory))
            {
                // Remote or generate a new one
                if (Properties.Settings.Default.UserGitRemote == true)
                {
                    // check if server is alive
                    if (IsGITAlive(Properties.Settings.Default.GitRemote) || IsHTTPSAlive(Properties.Settings.Default.GitRemote))
                    { 
                        try
                        {
                            string clonedRepoPath = Repository.Clone(Properties.Settings.Default.GitRemote, Properties.Settings.Default.PassDirectory, new CloneOptions()
                            {
                                CredentialsProvider = (_url, _user, _cred) => new UsernamePasswordCredentials
                                {
                                    Username = Properties.Settings.Default.GitUser,
                                    Password = Properties.Settings.Default.GitPass
                                }
                            });
                        }
                        catch
                        {
                            MessageBox.Show("Couldn't connect to remote git repository. Restart the program and try again.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            System.Environment.Exit(1);
                        }
                    } else
                    {
                        MessageBox.Show("Can't reach your GIT host", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        System.Environment.Exit(1);
                    }
                }
                else
                {
                    // creating new Git
                    var repo = Repository.Init(Properties.Settings.Default.PassDirectory, false);
                }
            }
            else
            {
                // Check if the remote is there
                if (IsGITAlive(Properties.Settings.Default.GitRemote) || IsHTTPSAlive(Properties.Settings.Default.GitRemote)) GITRemoveOffline = true;

                // Check if we have the latest if we have a remote
                if (Properties.Settings.Default.UserGitRemote == true && GITRemoveOffline == false)
                {
                    using (var repo = new Repository(Properties.Settings.Default.PassDirectory))
                    {
                        Signature Signature = new Signature("pass4win", "pull@pass4win.com", new DateTimeOffset(2011, 06, 16, 10, 58, 27, TimeSpan.FromHours(2)));
                        FetchOptions fetchOptions = new FetchOptions();
                        fetchOptions.CredentialsProvider = (_url, _user, _cred) => new UsernamePasswordCredentials
                                            {
                                                Username = Properties.Settings.Default.GitUser,
                                                Password = Properties.Settings.Default.GitPass
                                            };
                        MergeOptions mergeOptions = new MergeOptions();
                        PullOptions pullOptions = new PullOptions();
                        pullOptions.FetchOptions = fetchOptions;
                        pullOptions.MergeOptions = mergeOptions;
                        MergeResult mergeResult = repo.Network.Pull(Signature, pullOptions);
                    }
                }

            }

            // Init GPG if needed
            string gpgfile = Properties.Settings.Default.PassDirectory;
            gpgfile += "\\.gpg-id";
            // Check if we need to init the directory
            if (!File.Exists(gpgfile))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(gpgfile));
                KeySelect newKeySelect = new KeySelect();
                if (newKeySelect.ShowDialog() == DialogResult.OK)
                {
                    using (StreamWriter w = new StreamWriter(gpgfile))
                    {
                        w.Write(newKeySelect.gpgkey);
                    }
                    using (var repo = new Repository(Properties.Settings.Default.PassDirectory))
                    {
                        repo.Stage(gpgfile);
                        repo.Commit("gpgid added", new Signature("pass4win", "pass4win", System.DateTimeOffset.Now), new Signature("pass4win", "pass4win", System.DateTimeOffset.Now));
                    }
                }
                else
                {
                    MessageBox.Show("Need key...... Restart the program and try again.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    System.Environment.Exit(1);
                }
            }
            // Setting the exe location for the GPG Dll
            GpgInterface.ExePath = Properties.Settings.Default.GPGEXE;

            // Setting up datagrid
            dt.Columns.Add("colPath", typeof(string));
            dt.Columns.Add("colText", typeof(string));

            ListDirectory(new DirectoryInfo(Properties.Settings.Default.PassDirectory), "");

            dataPass.DataSource = dt.DefaultView;
            dataPass.Columns[0].Visible=false;

            EnableTray = true;
        }

        // Used for class access to the data
        private DataTable dt = new DataTable();
        // Class access to the tempfile
        private string tmpfile;
        // timer for clearing clipboard
        static System.Threading.Timer _timer;
        // UI Trayicon toggle
        private bool EnableTray;
        // Remote status of GIT
        private bool GITRemoveOffline;



        //
        // UI stuff
        //

        private void btnKeyManager_Click(object sender, EventArgs e)
        {
            frmKeyManager KeyManager = new frmKeyManager();
            KeyManager.Show();
        }

        private void btnAdd_Click(object sender, EventArgs e)
        {
            // get the new entryname
            InputBoxValidation validation = delegate(string val)
            {
                if (val == "")
                    return "Value cannot be empty.";
                if (new Regex(@"[a-zA-Z0-9-\\_]+/g").IsMatch(val))
                    return "Not a valid name, can only use characters or numbers and - \\.";
                if (File.Exists(Properties.Settings.Default.PassDirectory + "\\" + @val + ".gpg"))
                     return "Entry already exists.";
                return "";
            };

            string value = "";
            if (InputBox.Show("Enter a new name", "Name:", ref value, validation) == DialogResult.OK)
            {
                // parse path
                string tmpPath = Properties.Settings.Default.PassDirectory + "\\" + @value + ".gpg";;
                Directory.CreateDirectory(Path.GetDirectoryName(tmpPath));
                using (File.Create(tmpPath)) { }
                
                ResetDatagrid();
                // set the selected item.
                foreach (DataGridViewRow row in dataPass.Rows)
                {
                    if (row.Cells[1].Value.ToString().Equals(value))
                    {
                        dataPass.CurrentCell = row.Cells[1];
                        row.Selected = true;
                        break;
                    }
                }
                // add to git
                using (var repo = new Repository(Properties.Settings.Default.PassDirectory))
                {
                    // Stage the file
                    repo.Stage(tmpPath);
                }
                // dispose timer thread and clear ui.
                
                if (_timer != null) _timer.Dispose();
                statusPB.Visible = false;
                statusTxt.Text = "Ready";
                // Set the text detail to the correct state
                txtPassDetail.Text = "";
                txtPassDetail.ReadOnly = false;
                txtPassDetail.BackColor = Color.White;
                // dataPass.Enabled = false;
                txtPassDetail.Focus();
            }
        }

        // Search handler
        private void txtPass_TextChanged(object sender, EventArgs e)
        {
            dt.DefaultView.RowFilter = "colText LIKE '%" + txtPass.Text + "%'";
            if (dt.DefaultView.Count == 0)
            {
                txtPassDetail.Clear();
                // dispose timer thread and clear ui.
                if (_timer != null) _timer.Dispose();
                statusPB.Visible = false;
                statusTxt.Text = "Ready";
            }
        }

        // Decrypt the selected entry when pressing enter in textbox
        private void txtPass_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Return)
                if (dt.DefaultView.Count != 0)
                    decrypt_pass(dataPass.Rows[dataPass.CurrentCell.RowIndex].Cells[0].Value.ToString());
        }

        private void dataPass_SelectionChanged(object sender, EventArgs e)
        {
            if (dataPass.CurrentCell != null)
                decrypt_pass(dataPass.Rows[dataPass.CurrentCell.RowIndex].Cells[0].Value.ToString());

            btnMakeVisible.Visible = true;
            txtPassDetail.Visible = false;
        }


        private void txtPassDetail_Leave(object sender, EventArgs e)
        {
            if (txtPassDetail.ReadOnly == false)
            {
                txtPassDetail.ReadOnly = false;
                txtPassDetail.Visible = false;
                btnMakeVisible.Visible = true;
                txtPassDetail.BackColor = Color.LightGray;
                // read .gpg-id
                string gpgfile = Path.GetDirectoryName(dataPass.Rows[dataPass.CurrentCell.RowIndex].Cells[0].Value.ToString());
                gpgfile += "\\.gpg-id";
                // check if .gpg-id exists otherwise get the root .gpg-id
                if (!File.Exists(gpgfile))
                {
                    gpgfile = Properties.Settings.Default.PassDirectory;
                    gpgfile += "\\.gpg-id";
                }
                List<string> GPGRec = new List<string>() { };
                using (StreamReader r = new StreamReader(gpgfile))
                {
                    string line;
                    while ((line = r.ReadLine()) != null)
                    {
                        GPGRec.Add(line);
                    }
                }
                // match keyid
                List<GpgApi.KeyId> recipients = new List<KeyId>() { };
                foreach (var line in GPGRec)
                {
                    GpgListSecretKeys publicKeys = new GpgListSecretKeys();
                    publicKeys.Execute();
                    foreach (Key key in publicKeys.Keys)
                    {
                        if (key.UserInfos[0].Email == line.ToString())
                        {
                            recipients.Add(key.Id);
                        }
                    }
                }
                // encrypt
                string tmpFile = Path.GetTempFileName();
                string tmpFile2 = Path.GetTempFileName();

                using (StreamWriter w = new StreamWriter(tmpFile))
                {
                    w.Write(txtPassDetail.Text);
                }

                GpgEncrypt encrypt = new GpgEncrypt(tmpFile, tmpFile2, false, false, null, recipients, GpgApi.CipherAlgorithm.None);
                GpgInterfaceResult enc_result = encrypt.Execute();
                Encrypt_Callback(enc_result, tmpFile, tmpFile2, dataPass.Rows[dataPass.CurrentCell.RowIndex].Cells[0].Value.ToString());
            }
            
            dataPass.Enabled = true;
        }

        //
        // Decrypt functions
        //

        // Decrypt the file into a tempfile. 
        private void decrypt_pass(string path, bool clear = true)
        {
            FileInfo f = new FileInfo(path);
            if (f.Length > 0)
            {
                tmpfile = Path.GetTempFileName();
                GpgDecrypt decrypt = new GpgDecrypt(path, tmpfile);
                {
                    // The current thread is blocked until the decryption is finished.
                    GpgInterfaceResult result = decrypt.Execute();
                    Decrypt_Callback(result, clear);
                }
            }
        }

        // Callback for the encrypt thread
        public void Encrypt_Callback(GpgInterfaceResult result, string tmpFile, string tmpFile2, string path)
        {
            if (result.Status == GpgInterfaceStatus.Success)
            {
                File.Delete(tmpFile);
                File.Delete(path);
                File.Move(tmpFile2, path);
                // add to git
                using (var repo = new Repository(Properties.Settings.Default.PassDirectory))
                {
                    // Stage the file
                    repo.Stage(path);
                    // Commit
                    repo.Commit("password changes", new Signature("pass4win", "pass4win", System.DateTimeOffset.Now), new Signature("pass4win", "pass4win", System.DateTimeOffset.Now));
                    if (Properties.Settings.Default.UserGitRemote == true && GITRemoveOffline == false)
                    {
                        var remote = repo.Network.Remotes["origin"];
                        var options = new PushOptions();
                        options.CredentialsProvider = (_url, _user, _cred) => new UsernamePasswordCredentials
                        {
                            Username = Properties.Settings.Default.GitUser,
                            Password = Properties.Settings.Default.GitPass
                        }; 
                        var pushRefSpec = @"refs/heads/master";
                        repo.Network.Push(remote, pushRefSpec, options);
                    }
                }
            }
            else
            {
                MessageBox.Show("You shouldn't see this.... Awkward right... Encryption failed", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

         // Callback for the decrypt thread
        private void Decrypt_Callback(GpgInterfaceResult result, bool clear)
        {
            if (result.Status == GpgInterfaceStatus.Success)
            {
                txtPassDetail.Text = File.ReadAllText(this.tmpfile);
                File.Delete(tmpfile);
                // copy to clipboard
                if (txtPassDetail.Text != "")
                { 
                    Clipboard.SetText(new string(txtPassDetail.Text.TakeWhile(c => c != '\n').ToArray()));
                    if (clear)
                    {
                        // set progressbar as notification
                        statusPB.Maximum = 45;
                        statusPB.Value = 0;
                        statusPB.Step = 1;
                        statusPB.Visible = true;
                        statusTxt.Text = "Countdown to clearing clipboard  ";
                        //Create the timer
                        _timer = new System.Threading.Timer(ClearClipboard, null, 0, 1000);
                    }
                }
            }
            else
            {
                txtPassDetail.Text = "Something went wrong.....";
            }
        }

        //
        // All the menu options for the datagrid
        //
        private void editToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // dispose timer thread and clear ui.
            if (_timer != null) _timer.Dispose();
            statusPB.Visible = false;
            statusTxt.Text = "Ready";
            // make control editable, give focus and content
            decrypt_pass(dataPass.Rows[dataPass.CurrentCell.RowIndex].Cells[0].Value.ToString(),false);
            txtPassDetail.ReadOnly = false;
            txtPassDetail.Visible = true;
            btnMakeVisible.Visible = false;
            txtPassDetail.BackColor = Color.White;
            txtPassDetail.Focus();
        }

        private void renameToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // rename the entry
            InputBoxValidation validation = delegate(string val)
            {
                if (val == "")
                    return "Value cannot be empty.";
                if (new Regex(@"[a-zA-Z0-9-\\_]+/g").IsMatch(val))
                    return "Not a valid name, can only use characters or numbers and - \\.";
                if (File.Exists(Properties.Settings.Default.PassDirectory + "\\" + @val + ".gpg"))
                    return "Entry already exists.";
                return "";
            };

            string value = dataPass.Rows[dataPass.CurrentCell.RowIndex].Cells[1].Value.ToString();
            if (InputBox.Show("Enter a new name", "Name:", ref value, validation) == DialogResult.OK)
            {
                // parse path
                string tmpPath = Properties.Settings.Default.PassDirectory + "\\" + @value + ".gpg";
                Directory.CreateDirectory(Path.GetDirectoryName(tmpPath));
                File.Copy(dataPass.Rows[dataPass.CurrentCell.RowIndex].Cells[0].Value.ToString(), tmpPath);
                using (var repo = new Repository(Properties.Settings.Default.PassDirectory))
                {
                    // add the file
                    repo.Remove(dataPass.Rows[dataPass.CurrentCell.RowIndex].Cells[0].Value.ToString());
                    repo.Stage(tmpPath);
                    // Commit
                    repo.Commit("password moved", new Signature("pass4win", "pass4win", System.DateTimeOffset.Now), new Signature("pass4win", "pass4win", System.DateTimeOffset.Now));

                    if (Properties.Settings.Default.UserGitRemote == true && GITRemoveOffline == false)
                    {
                        //push
                        var remote = repo.Network.Remotes["origin"];
                        var options = new PushOptions();
                        options.CredentialsProvider = (_url, _user, _cred) => new UsernamePasswordCredentials
                        {
                            Username = Properties.Settings.Default.GitUser,
                            Password = Properties.Settings.Default.GitPass
                        };
                        var pushRefSpec = @"refs/heads/master";
                        repo.Network.Push(remote, pushRefSpec, options);
                    }
                }
                ResetDatagrid();

            }

        }

        private void deleteToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // remove from git
            using (var repo = new Repository(Properties.Settings.Default.PassDirectory))
            {
                // remove the file
                repo.Remove(dataPass.Rows[dataPass.CurrentCell.RowIndex].Cells[0].Value.ToString());
                // Commit
                repo.Commit("password removed", new Signature("pass4win", "pass4win", System.DateTimeOffset.Now), new Signature("pass4win", "pass4win", System.DateTimeOffset.Now));

                if (Properties.Settings.Default.UserGitRemote == true && GITRemoveOffline == false)
                {
                    // push
                    var remote = repo.Network.Remotes["origin"];
                    var options = new PushOptions();
                    options.CredentialsProvider = (_url, _user, _cred) => new UsernamePasswordCredentials
                    {
                        Username = Properties.Settings.Default.GitUser,
                        Password = Properties.Settings.Default.GitPass
                    };
                    var pushRefSpec = @"refs/heads/master";
                    repo.Network.Push(remote, pushRefSpec, options);
                }
            }
            ResetDatagrid();
        }

        //
        // Generic / Util functions
        //

        // clear the clipboard and make txt invisible
        void ClearClipboard(object o)
        {
            if (statusPB.Value == 45)
            {
                this.BeginInvoke((Action)(() => Clipboard.Clear()));
                this.BeginInvoke((Action)(() => statusPB.Visible = false));
                this.BeginInvoke((Action)(() => statusTxt.Text = "Ready"));
                this.BeginInvoke((Action)(() => statusPB.Value = 0));
                this.BeginInvoke((Action)(() => btnMakeVisible.Visible = true));
                this.BeginInvoke((Action)(() => txtPassDetail.Visible = false));
            }
            else if (statusTxt.Text != "Ready")
            {
                this.BeginInvoke((Action)(() => statusPB.PerformStep()));
            }
            
        }

        // reset the datagrid (clear & Fill)
        private void ResetDatagrid(){
            dt.Clear();
            processDirectory(Properties.Settings.Default.PassDirectory);
            ListDirectory(new DirectoryInfo(Properties.Settings.Default.PassDirectory), "");
        }

        // Fill the datagrid
        private void ListDirectory(DirectoryInfo path, string prefix)
        {
            foreach (var directory in path.GetDirectories())
            {
                if (!directory.Name.StartsWith("."))
                {
                    string tmpPrefix;
                    if (prefix != "")
                    {
                        tmpPrefix = prefix + "\\" + directory;
                    }
                    else
                    {
                        tmpPrefix = prefix + directory;
                    }
                    ListDirectory(directory, tmpPrefix);
                }
            }

            foreach (var ffile in path.GetFiles())
                if (!ffile.Name.StartsWith("."))
                {
                    if (ffile.Extension.ToLower() == ".gpg")
                    {
                        DataRow newItemRow = dt.NewRow();

                        newItemRow["colPath"] = ffile.FullName;
                        if (prefix != "")
                            newItemRow["colText"] = prefix + "\\" + Path.GetFileNameWithoutExtension(ffile.Name);
                        else
                            newItemRow["colText"] = Path.GetFileNameWithoutExtension(ffile.Name);

                        dt.Rows.Add(newItemRow);
                    }
                }
        }

        // cleanup script to remove empty directories from the password store
        private static void processDirectory(string startLocation)
        {
            foreach (var directory in Directory.GetDirectories(startLocation))
            {
                processDirectory(directory);
                if (Directory.GetFiles(directory).Length == 0 && Directory.GetDirectories(directory).Length == 0)
                {
                    Directory.Delete(directory, false);
                }
            }
        }

        private void btnMakeVisible_Click(object sender, EventArgs e)
        {
            btnMakeVisible.Visible = false;
            txtPassDetail.Visible = true;
        }

        private void frmMain_Resize(object sender, EventArgs e)
        {
            if (FormWindowState.Minimized == this.WindowState && EnableTray == true)
            {
                notifyIcon1.Visible = true;
                notifyIcon1.ShowBalloonTip(500);
                this.Hide();
            }

            else if (FormWindowState.Normal == this.WindowState)
            {
                notifyIcon1.Visible = false;
            }
        }

        private void notifyIcon1_MouseClick(object sender, MouseEventArgs e)
        {
            this.Show();
            this.WindowState = FormWindowState.Normal;
        }

        private void button1_Click(object sender, EventArgs e)
        {
            frmConfig Config = new frmConfig();
            Config.Show();
        }

        public static bool IsGITAlive(String hostName)
        {
            try
            {
                using (TcpClient tcpClient = new TcpClient())
                {
                    tcpClient.Connect(hostName, 9418);
                    return true;
                }
            }
            catch (SocketException)
            {

                return false;
            }
        }

        public static bool IsHTTPSAlive(String hostName)
        {
            try
            {
                using (TcpClient tcpClient = new TcpClient())
                {
                    tcpClient.Connect(hostName, 443);
                    return true;
                }
            }
            catch (SocketException)
            {

                return false;
            }
        }
    }

    public class InputBox
    {
        public static DialogResult Show(string title, string promptText, ref string value)
        {
            return Show(title, promptText, ref value, null);
        }

        public static DialogResult Show(string title, string promptText, ref string value,
                                        InputBoxValidation validation)
        {
            Form form = new Form();
            Label label = new Label();
            TextBox textBox = new TextBox();
            Button buttonOk = new Button();
            Button buttonCancel = new Button();

            form.Text = title;
            label.Text = promptText;
            textBox.Text = value;

            buttonOk.Text = "OK";
            buttonCancel.Text = "Cancel";
            buttonOk.DialogResult = DialogResult.OK;
            buttonCancel.DialogResult = DialogResult.Cancel;

            label.SetBounds(9, 20, 372, 13);
            textBox.SetBounds(12, 36, 372, 20);
            buttonOk.SetBounds(228, 72, 75, 23);
            buttonCancel.SetBounds(309, 72, 75, 23);

            label.AutoSize = true;
            textBox.Anchor = textBox.Anchor | AnchorStyles.Right;
            buttonOk.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            buttonCancel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;

            form.ClientSize = new Size(396, 107);
            form.Controls.AddRange(new Control[] { label, textBox, buttonOk, buttonCancel });
            form.ClientSize = new Size(Math.Max(300, label.Right + 10), form.ClientSize.Height);
            form.FormBorderStyle = FormBorderStyle.FixedDialog;
            form.StartPosition = FormStartPosition.CenterScreen;
            form.MinimizeBox = false;
            form.MaximizeBox = false;
            form.AcceptButton = buttonOk;
            form.CancelButton = buttonCancel;
            if (validation != null)
            {
                form.FormClosing += delegate(object sender, FormClosingEventArgs e)
                {
                    if (form.DialogResult == DialogResult.OK)
                    {
                        string errorText = validation(textBox.Text);
                        if (e.Cancel = (errorText != ""))
                        {
                            MessageBox.Show(form, errorText, "Validation Error",
                                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                            textBox.Focus();
                        }
                    }
                };
            }

            form.Shown += delegate(object sender, EventArgs e)
            {
                form.TopMost = true;
                form.Activate();
            };

            DialogResult dialogResult = form.ShowDialog();
            value = textBox.Text;
            return dialogResult;
        }
    }
    public delegate string InputBoxValidation(string errorMessage);
}

