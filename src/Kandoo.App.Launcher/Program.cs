using System.Windows.Forms;
using Kandoo.App.Launcher;

Application.EnableVisualStyles();
Application.SetCompatibleTextRenderingDefault(false);
Application.Run(new MainForm(new LocalProfileStore()));
