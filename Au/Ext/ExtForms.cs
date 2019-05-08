﻿//Why Forms extension methods are in separate class, not in ExtOther?
//Initially it was in ExtOther. Then I noticed: appdomains are loaded much slower if the code uses ExtOther.Has.
//Reason: .NET loaded Forms and Drawing dlls, although Has does not use them. Moving Forms extensions to a separate class fixed it.

using System;
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
using System.Runtime.ExceptionServices;
//using System.Linq;
using System.Xml.Linq;
using System.Windows.Forms;
using System.Drawing;

using Au.Types;
using static Au.NoClass;

namespace Au
{
	/// <summary>
	/// Adds extension methods to some .NET classes.
	/// </summary>
	public static class ExtForms
	{
		#region Control

		/// <summary>
		/// If control handle still not created, creates. Does not create child control handles.
		/// Like <see cref="Control.CreateHandle"/>, which is protected.
		/// Unlike <see cref="Control.CreateControl"/>, creates handle even if invisible.
		/// </summary>
		public static void CreateHandleNow(this Control t)
		{
			if(!t.IsHandleCreated) {
				_ = t.Handle;
			}
		}

		/// <summary>
		/// Creates handle of this control/form and descendant controls.
		/// Unlike Control.CreateHandle, works when invisible.
		/// </summary>
		/// <remarks>
		/// Uses similar code as .NET Controls.cs internal void CreateControl(bool fIgnoreVisible).
		/// Does not support controls with created handles. Asserts and throws. Would need to set parent handle, but it is a private method. That is why this func is not public.
		/// </remarks>
		internal static void CreateControlNow(this Control t/*, int level = 0*/)
		{
			//Print(new string(' ', level) + t.ToString());
			Debug.Assert(!t.IsHandleCreated); if(t.IsHandleCreated) throw new InvalidOperationException("Control handle already created: " + t);
			t.CreateHandleNow();
			if(t.HasChildren) {
				var cc = t.Controls;
				var a = new Control[cc.Count]; cc.CopyTo(a, 0);
				foreach(var c in a) {
					CreateControlNow(c/*, level+1*/);
				}
			}
		}

		/// <summary>
		/// Gets mouse cursor position in client area coordinates.
		/// Returns default(POINT) if handle not created.
		/// </summary>
		public static POINT MouseClientXY(this Control t)
		{
			return ((Wnd)t).MouseClientXY;
		}

		/// <summary>
		/// Gets mouse cursor position in window coordinates.
		/// </summary>
		public static POINT MouseWindowXY(this Control t)
		{
			POINT p = Mouse.XY;
			POINT k = t.Location;
			return (p.x - k.x, p.y - k.y);
		}

		/// <summary>
		/// Sets the textual cue, or tip, that is displayed by the control when it does not have text.
		/// Sends API <msdn>EM_SETCUEBANNER</msdn>.
		/// Does nothing if Multiline.
		/// </summary>
		public static void SetCueBanner(this TextBox t, string text, bool showWhenFocused = false)
		{
			Debug.Assert(!t.Multiline);
			_SetCueBanner(t, Api.EM_SETCUEBANNER, showWhenFocused, text);
		}

		/// <summary>
		/// Sets the textual cue, or tip, that is displayed by the edit control when it does not have text.
		/// Sends API <msdn>CB_SETCUEBANNER</msdn>.
		/// </summary>
		public static void SetCueBanner(this ComboBox t, string text)
		{
			_SetCueBanner(t, Api.CB_SETCUEBANNER, false, text);
		}

		static void _SetCueBanner(Control c, int message, bool showWhenFocused, string text)
		{
			if(c.IsHandleCreated) {
				((Wnd)c).SendS(message, showWhenFocused, text);
			} else if(!Empty(text)) {
				c.HandleCreated += (unu, sed) => _SetCueBanner(c, message, showWhenFocused, text);
			}
		}

		//Currently not used.
		//note: when any function of this class is used, the presence of this function somehow makes to load Forms and Drawing dlls at run time, even if that function does not use Forms.
		///// <summary>
		///// Creates a control, sets its commonly used properties (Bounds, Text, tooltip, Anchor) and adds it to the Controls collection of this.
		///// </summary>
		///// <typeparam name="T">Control class.</typeparam>
		///// <param name="t"></param>
		///// <param name="x">Left.</param>
		///// <param name="y">Top.</param>
		///// <param name="width">Width.</param>
		///// <param name="height">Height.</param>
		///// <param name="text">The <see cref="Control.Text"/> property.</param>
		///// <param name="tooltip">Tooltip text.
		///// This function creates a ToolTip component and assigns it to the Tag property of this.</param>
		///// <param name="anchor">The <see cref="Control.Anchor"/> property.</param>
		//public static T AddChild<T>(this ContainerControl t, int x, int y, int width, int height, string text = null, string tooltip = null, AnchorStyles anchor = AnchorStyles.None/*, string name = null*/) where T : Control, new()
		//{
		//	var c = new T();
		//	//if(!Empty(name)) c.Name = name;
		//	c.Bounds = new System.Drawing.Rectangle(x, y, width, height);
		//	if(anchor != AnchorStyles.None) c.Anchor = anchor;
		//	if(text != null) c.Text = text;
		//	if(!Empty(tooltip)) {
		//		var tt = t.Tag as ToolTip;
		//		if(tt == null) {
		//			t.Tag = tt = new ToolTip();
		//			//t.Disposed += (o, e) => Print((o as ContainerControl).Tag as ToolTip);
		//			//t.Disposed += (o, e) => ((o as ContainerControl).Tag as ToolTip)?.Dispose(); //it seems tooltip is auto-disposed when its controls are disposed. Anyway, this event is only if the form is disposed explicitly, but nobody does it.
		//		}
		//		tt.SetToolTip(c, tooltip);
		//	}
		//	t.Controls.Add(c);
		//	return c;
		//}

		#endregion
	}
}
