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
//using System.Linq;

using Au;
using Au.Types;

namespace Au.Types
{
#pragma warning disable 1591 //missing doc

	/// <summary>
	/// Modifier keys as flags.
	/// </summary>
	/// <remarks>
	/// The values don't match those in the .NET enum <see cref="System.Windows.Forms.Keys"/>. This library does not use the .NET enum for modifier keys, mostly because it: does not have Win as modifier flag; confusing names, for example Alt and Menu.
	/// </remarks>
	/// <seealso cref="AKeys.More.KModToKeys"/>
	/// <seealso cref="AKeys.More.KModFromKeys"/>
	/// <seealso cref="KKey"/>
	[Flags]
	public enum KMod :byte
	{
		Shift = 1,
		Ctrl = 2,
		Alt = 4,
		Win = 8,
	}

	/// <summary>
	/// Virtual-key codes.
	/// </summary>
	/// <remarks>
	/// The values are the same as the native VK_ constants. Also the same as in the <see cref="System.Windows.Forms.Keys"/> enum, but not as in the WPF <b>Key</b> enum.
	/// Some key names are different than VK_/Keys, for example Alt instead of VK_MENU/Menu.
	/// Most rare and obsolete keys are not included. You can use Keys or VK_ (int) like <c>(KKey)Keys.Attn</c>.
	/// This library does not use the .NET <b>Keys</b> enum, mostly because it includes modifier key flags and it's easy to confuse eg Shift (flag) with ShiftKey (key). Also this library does not use the WPF <b>Key</b> enum; its values don't match the native VK_ constants that must be used with API functions.
	/// </remarks>
	/// <seealso cref="KMod"/>
	public enum KKey :byte
	{
		MouseLeft = 0x01,
		MouseRight = 0x02,
		///<summary>Ctrl+Pause.</summary>
		Break = 0x03,
		MouseMiddle = 0x04,
		MouseX1 = 0x05,
		MouseX2 = 0x06,
		Back = 0x08,
		Tab = 0x09,
		///<summary>Shift+NumPad5, or NumPad5 when NumLock off.</summary>
		Clear = 0x0C,
		Enter = 0x0D,
		Shift = 0x10,
		Ctrl = 0x11,
		Alt = 0x12,
		Pause = 0x13,
		CapsLock = 0x14,
		IMEKanaMode = 0x15,
		IMEHangulMode = 0x15,
		IMEJunjaMode = 0x17,
		IMEFinalMode = 0x18,
		IMEHanjaMode = 0x19,
		IMEKanjiMode = 0x19,
		Escape = 0x1B,
		IMEConvert = 0x1C,
		IMENonconvert = 0x1D,
		IMEAccept = 0x1E,
		IMEModeChange = 0x1F,
		Space = 0x20,
		PageUp = 0x21,
		PageDown = 0x22,
		End = 0x23,
		Home = 0x24,
		Left = 0x25,
		Up = 0x26,
		Right = 0x27,
		Down = 0x28,
		//Select = 0x29,
		//Print = 0x2A,
		//Execute= 0x2B,
		PrintScreen = 0x2C,
		Insert = 0x2D,
		Delete = 0x2E,
		//Help = 0x2F,
		///<summary>The 0 ) key.</summary>
		D0 = 0x30,
		///<summary>The 1 ! key.</summary>
		D1 = 0x31,
		///<summary>The 2 @ key.</summary>
		D2 = 0x32,
		///<summary>The 3 # key.</summary>
		D3 = 0x33,
		///<summary>The 4 $ key.</summary>
		D4 = 0x34,
		///<summary>The 5 % key.</summary>
		D5 = 0x35,
		///<summary>The 6 ^ key.</summary>
		D6 = 0x36,
		///<summary>The 7 &amp; key.</summary>
		D7 = 0x37,
		///<summary>The 8 * key.</summary>
		D8 = 0x38,
		///<summary>The 9 ( key.</summary>
		D9 = 0x39,
		A = 0x41,
		B = 0x42,
		C = 0x43,
		D = 0x44,
		E = 0x45,
		F = 0x46,
		G = 0x47,
		H = 0x48,
		I = 0x49,
		J = 0x4A,
		K = 0x4B,
		L = 0x4C,
		M = 0x4D,
		N = 0x4E,
		O = 0x4F,
		P = 0x50,
		Q = 0x51,
		R = 0x52,
		S = 0x53,
		T = 0x54,
		U = 0x55,
		V = 0x56,
		W = 0x57,
		X = 0x58,
		Y = 0x59,
		Z = 0x5A,
		///<summary>The left Win key.</summary>
		Win = 0x5B,
		///<summary>The right Win key.</summary>
		RWin = 0x5C,
		///<summary>The Application/Menu key.</summary>
		Apps = 0x5D,
		Sleep = 0x5F,
		NumPad0 = 0x60,
		NumPad1 = 0x61,
		NumPad2 = 0x62,
		NumPad3 = 0x63,
		NumPad4 = 0x64,
		NumPad5 = 0x65,
		NumPad6 = 0x66,
		NumPad7 = 0x67,
		NumPad8 = 0x68,
		NumPad9 = 0x69,
		///<summary>The numpad * key.</summary>
		Multiply = 0x6A,
		///<summary>The numpad + key.</summary>
		Add = 0x6B,
		//Separator = 0x6C,
		///<summary>The numpad - key.</summary>
		Subtract = 0x6D,
		///<summary>The numpad . key.</summary>
		Decimal = 0x6E,
		///<summary>The numpad / key.</summary>
		Divide = 0x6F,
		F1 = 0x70,
		F2 = 0x71,
		F3 = 0x72,
		F4 = 0x73,
		F5 = 0x74,
		F6 = 0x75,
		F7 = 0x76,
		F8 = 0x77,
		F9 = 0x78,
		F10 = 0x79,
		F11 = 0x7A,
		F12 = 0x7B,
		F13 = 0x7C,
		F14 = 0x7D,
		F15 = 0x7E,
		F16 = 0x7F,
		F17 = 0x80,
		F18 = 0x81,
		F19 = 0x82,
		F20 = 0x83,
		F21 = 0x84,
		F22 = 0x85,
		F23 = 0x86,
		F24 = 0x87,
		//VK_NAVIGATION_VIEW ... VK_NAVIGATION_CANCEL
		NumLock = 0x90,
		ScrollLock = 0x91,
		//VK_OEM_NEC_EQUAL ... VK_OEM_FJ_ROYA
		///<summary>The left Shift key.</summary>
		LShift = 0xA0,
		///<summary>The right Shift key.</summary>
		RShift = 0xA1,
		///<summary>The left Ctrl key.</summary>
		LCtrl = 0xA2,
		///<summary>The right Ctrl key.</summary>
		RCtrl = 0xA3,
		///<summary>The left Alt key.</summary>
		LAlt = 0xA4,
		///<summary>The right Alt key.</summary>
		RAlt = 0xA5,
		BrowserBack = 0xA6,
		BrowserForward = 0xA7,
		BrowserRefresh = 0xA8,
		BrowserStop = 0xA9,
		BrowserSearch = 0xAA,
		BrowserFavorites = 0xAB,
		BrowserHome = 0xAC,
		VolumeMute = 0xAD,
		VolumeDown = 0xAE,
		VolumeUp = 0xAF,
		MediaNextTrack = 0xB0,
		MediaPrevTrack = 0xB1,
		MediaStop = 0xB2,
		MediaPlayPause = 0xB3,
		LaunchMail = 0xB4,
		LaunchMediaSelect = 0xB5,
		LaunchApp1 = 0xB6,
		LaunchApp2 = 0xB7,
		OemSemicolon = 0xBA,
		OemPlus = 0xBB,
		OemComma = 0xBC,
		OemMinus = 0xBD,
		OemPeriod = 0xBE,
		OemQuestion = 0xBF,
		OemTilde = 0xC0,
		//VK_GAMEPAD_A ... VK_GAMEPAD_RIGHT_THUMBSTICK_LEFT
		OemOpenBrackets = 0xDB,
		OemPipe = 0xDC,
		OemCloseBrackets = 0xDD,
		OemQuotes = 0xDE,
		//VK_OEM_8 ... VK_ICO_00
		IMEProcessKey = 0xE5,
		//VK_ICO_CLEAR
		///<summary>VK_PACKET. Not a key.</summary>
		Packet = 0xE7,
		//VK_OEM_RESET ... VK_OEM_BACKTAB
		//Attn = 0xF6,
		//Crsel = 0xF7,
		//Exsel = 0xF8,
		//EraseEof = 0xF9,
		//Play = 0xFA,
		//Zoom = 0xFB,
		//NoName = 0xFC,
		//Pa1 = 0xFD,
		//OemClear = 0xFE,
	}

#pragma warning restore 1591

	/// <summary>
	/// Used as an argument type with <see cref="AKeys.Key(KKeysEtc[])"/> and similar functions to send text instead of keys. See example.
	/// </summary>
	/// <example>
	/// <code><![CDATA[
	/// Key((KText)"user", "Tab", (KText)"password", "Enter");
	/// ]]></code>
	/// </example>
	public struct KText
	{
		string _text;
		KText(string s) => _text = s;
		///
		public static implicit operator KText(string s) => new KText(s);
		///
		public static implicit operator string(KText s) => s._text;
	}

	/// <summary>
	/// Parameter type of <see cref="AKeys.Key"/> and similar functions.
	/// Has implicit conversions from string, KText, KKey, int, Action, tuple (int, bool) and tuple (KKey, int, bool).
	/// </summary>
	public struct KKeysEtc
	{
		readonly object _o;
		KKeysEtc(object o) { _o = o; }

		/// <summary>
		/// Raw value.
		/// </summary>
		public object Value => _o;

		///
		public static implicit operator KKeysEtc(string s)=>new KKeysEtc(s);

		///
		public static implicit operator KKeysEtc(KText s) => new KKeysEtc(s);

		///
		public static implicit operator KKeysEtc(KKey k) => new KKeysEtc(k);

		///
		public static implicit operator KKeysEtc(int ms) => new KKeysEtc(ms);

		///
		public static implicit operator KKeysEtc(Action a) => new KKeysEtc(a);

		///
		public static implicit operator KKeysEtc((int scan, bool ext) t) => new KKeysEtc(t);

		///
		public static implicit operator KKeysEtc((KKey k, int scan, bool ext) t) => new KKeysEtc(t);
	}

	/// <summary>
	/// Defines a hotkey as <see cref="KMod"/> and <see cref="KKey"/>.
	/// Has implicit conversion operators from string like "Ctrl+Shift+K", tuple (KMod, KKey), enum KKey, enum Keys.
	/// </summary>
	public struct KHotkey
	{
		/// <summary>
		/// Modifier keys (flags).
		/// </summary>
		public KMod Mod { get; set; }

		/// <summary>
		/// Key without modifier keys.
		/// </summary>
		public KKey Key { get; set; }

		///
		public KHotkey(KMod mod, KKey key) { Mod = mod; Key = key; }

		/// <summary>Implicit conversion from string like "Ctrl+Shift+K".</summary>
		/// <exception cref="ArgumentException">"Error in hotkey."</exception>
		public static implicit operator KHotkey(string hotkey)
		{
			if(!AKeys.More.ParseHotkeyString(hotkey, out var mod, out var key)) throw new ArgumentException("Error in hotkey.");
			return new KHotkey(mod, key);
		}

		/// <summary>Implicit conversion from tuple (KMod, KKey).</summary>
		public static implicit operator KHotkey((KMod, KKey) hotkey) => new KHotkey(hotkey.Item1, hotkey.Item2);

		/// <summary>Implicit conversion from <see cref="KKey"/> (hotkey without modifiers).</summary>
		public static implicit operator KHotkey(KKey key) => new KHotkey(0, key);

		/// <summary>Implicit conversion from <see cref="System.Windows.Forms.Keys"/> like <c>Keys.Ctrl|Keys.B</c>.</summary>
		public static implicit operator KHotkey(System.Windows.Forms.Keys hotkey) => new KHotkey(AKeys.More.KModFromKeys(hotkey), (KKey)(byte)hotkey);

		/// <summary>Explicit conversion to <see cref="System.Windows.Forms.Keys"/>.</summary>
		public static explicit operator System.Windows.Forms.Keys(KHotkey hk) => AKeys.More.KModToKeys(hk.Mod) | (System.Windows.Forms.Keys)hk.Key;

		/// <summary>Allows to split a <b>KHotkey</b> variable like <c>var (mod, key) = hotkey;</c></summary>
		public void Deconstruct(out KMod mod, out KKey key) { mod = Mod; key = Key; }
	}
}
