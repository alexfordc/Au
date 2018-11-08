#define ADMIN_TRIGGERS

#if ADMIN_TRIGGERS
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Text;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Reflection;
using Microsoft.Win32;
using System.Runtime.ExceptionServices;
//using System.Linq;
//using System.Xml.Linq;

using Au;
using Au.Types;
using static Au.NoClass;
using Au.Triggers;

class TriggersInTasks
{
	Triggers _triggers;

	public TriggersInTasks(Wnd wManager)
	{
		Output.LibWriteQM2("TriggersInTasks ctor");

	}

	public void Dispose()
	{
		Output.LibWriteQM2("TriggersInTasks.Dispose");
		Stop();
	}

	public void Start(string dbPath)
	{
		Output.LibWriteQM2("TriggersInTasks.Start, " + dbPath);
		Debug.Assert(_triggers == null);
		_triggers = new Triggers(dbPath);

	}

	public void Stop()
	{
		if(_triggers != null) {
			Output.LibWriteQM2("TriggersInTasks.Stop");
			_triggers.Dispose();
			_triggers = null;
		}

	}
}
#endif
