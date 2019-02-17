//{{
//{{ using
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Drawing;
using System.Linq;
using Au;
using Au.Types;
using static Au.NoClass;
using Au.Triggers;
//}}

//{{ class, Main
unsafe partial class App :AuApp { //}}
[STAThread] static void Main(string[] args) { new App()._Main(args); }
void _Main(string[] args) { //}}
//}}
//}}

//This is a C# script.
//To compile and run it, click the Run button on the toolbar.

Print("Function Print writes text and variables to the output pane.");

AuDialog.Show("Example", "Message box.");