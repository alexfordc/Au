﻿using System;
using System.Collections.Generic;
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
//using System.Windows.Forms;
//using System.Drawing;
//using System.Linq;

using Au.Types;

namespace Au
{
	public partial class AToolbar
	{
		class _Settings : Util.JSettings
		{
			public static _Settings Load(string file, bool useDefault = false) => _Load<_Settings>(file, useDefault);

			public TBBorder border { get => _border; set => Set2(ref _border, value); }
			TBBorder _border = TBBorder.Width2;

			public int borderColor { get => _borderColor; set => Set(ref _borderColor, value); }
			int _borderColor; //not ColorInt because in JSON it is saved as struct

			public TBAnchor anchor { get => _anchor; set => Set2(ref _anchor, value); }
			TBAnchor _anchor = TBAnchor.TopLeft;

			public TBLocation location { get => _location; set => Set(ref _location, value); }
			TBLocation _location; // = new TBLocation { Left = 150, Top = 5, Right = 7, Bottom = 7 };

			public bool sizable { get => _sizable; set => Set(ref _sizable, value); }
			bool _sizable = true;

			public SIZE size { get => _size; set => Set(ref _size, value); }
			SIZE _size = (_DefaultWidth, _DefaultHeight);

			public bool autoSize { get => _autoSize; set => Set(ref _autoSize, value); }
			bool _autoSize;

			public int wrapWidth { get => _wrapWidth; set => Set(ref _wrapWidth, value); }
			int _wrapWidth;

			public int screen { get => _screen; set => Set(ref _screen, value); }
			int _screen;

			public TBFlags miscFlags { get => _miscFlags; set => Set2(ref _miscFlags, value); }
			TBFlags _miscFlags;
		}

		static int _DefaultWidth => 150;
		static int _DefaultHeight => Util.ADpi.ScaleInt(16) + 10;
	}
}
