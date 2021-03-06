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

using Au.Types;

namespace Au
{
	/// <summary>
	/// Allocates, writes and reads memory in other process.
	/// </summary>
	/// <remarks>
	/// Must be disposed. Example: <c>using(var pm=new AProcessMemory(...)) { ... }</c>.
	/// </remarks>
	public unsafe class AProcessMemory : IDisposable
	{
		Handle_ _hproc;
		HandleRef _HprocHR => new HandleRef(this, _hproc);

		///
		protected virtual void Dispose(bool disposing)
		{
			if(_hproc.Is0) return;
			if(Mem != default) {
				var mem = Mem; Mem = default;
				if(!_dontFree) {
					if(!Api.VirtualFreeEx(_HprocHR, mem)) AWarning.Write("Failed to free process memory. " + ALastError.Message);
				}
			}
			_hproc.Dispose();
		}

		///
		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		///
		~AProcessMemory() => Dispose(false);

		/// <summary>
		/// Process handle.
		/// Opened with access PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_VM_WRITE.
		/// </summary>
		public IntPtr ProcessHandle => _hproc;

		/// <summary>
		/// Address of memory allocated in that process.
		/// </summary>
		/// <remarks>
		/// The address is invalid in this process.
		/// </remarks>
		public IntPtr Mem { get; private set; }

		/// <summary>
		/// Sets an address of memory in that process that is to be used by the read and write functions.
		/// </summary>
		/// <param name="mem">A memory address in that process.</param>
		/// <param name="freeWhenDisposing">
		/// Let the Dispose method (or finalizer) call API <msdn>VirtualFreeEx</msdn> to free mem. The memory must be allocated with API <msdn>VirtualAllocEx</msdn> (by any process) or <msdn>VirtualAlloc</msdn> (by that process).
		/// If false, mem can be any memory in that process, and this variable will not free it. Alternatively you can use <see cref="ReadOther"/> and <see cref="WriteOther"/>.</param>
		/// <exception cref="InvalidOperationException">This variable already has Mem, unless it was set by this function with <i>freeWhenDisposing</i> = false.</exception>
		/// <remarks>
		/// This function can be used if this variable was created with <i>nBytes</i> = 0. Else exception. Also exception if this function previously called with <i>freeWhenDisposing</i> = true.
		/// </remarks>
		public void SetMem(IntPtr mem, bool freeWhenDisposing)
		{
			if(Mem != default && !_dontFree) throw new InvalidOperationException();
			_dontFree = !freeWhenDisposing;
			Mem = mem;
		}
		bool _dontFree;

		void _Alloc(int pid, AWnd w, int nBytes)
		{
			string err;
			const uint fl = Api.PROCESS_VM_OPERATION | Api.PROCESS_VM_READ | Api.PROCESS_VM_WRITE;
			_hproc = w.Is0 ? Handle_.OpenProcess(pid, fl) : Handle_.OpenProcess(w, fl);
			if(_hproc.Is0) { err = "Failed to open process handle."; goto ge; }

			if(nBytes != 0) {
				Mem = Api.VirtualAllocEx(_HprocHR, default, nBytes);
				if(Mem == default) { err = "Failed to allocate process memory."; goto ge; }
			}
			return;
			ge:
			var e = new AuException(0, err);
			Dispose();
			throw e;
		}

		/// <summary>
		/// Opens window's process handle and optionally allocates memory in that process.
		/// </summary>
		/// <param name="w">A window in that process.</param>
		/// <param name="nBytes">If not 0, allocates this number of bytes of memory in that process.</param>
		/// <remarks>This is the preferred constructor when the process has windows. It works with windows of [](xref:uac) High integrity level when this process is Medium+uiAccess.</remarks>
		/// <exception cref="AuWndException">w invalid.</exception>
		/// <exception cref="AuException">Failed to open process handle (usually because of UAC) or allocate memory.</exception>
		public AProcessMemory(AWnd w, int nBytes)
		{
			w.ThrowIfInvalid();
			_Alloc(0, w, nBytes);
		}

		/// <summary>
		/// Opens window's process handle and optionally allocates memory in that process.
		/// </summary>
		/// <param name="processId">Process id.</param>
		/// <param name="nBytes">If not 0, allocates this number of bytes of memory in that process.</param>
		/// <exception cref="AuException">Failed to open process handle (usually because of [](xref:uac)) or allocate memory.</exception>
		public AProcessMemory(int processId, int nBytes)
		{
			_Alloc(processId, default, nBytes);
		}

		/// <summary>
		/// Copies a string from this process to the memory allocated in that process by the constructor.
		/// In that process the string is written as '\0'-terminated UTF-16 string. For it is used (s.Length+1)*2 bytes of memory in that process (+1 for the '\0', *2 because UTF-16 character size is 2 bytes).
		/// Returns false if fails.
		/// </summary>
		/// <param name="s">A string in this process.</param>
		/// <param name="offsetBytes">Offset in the memory allocated by the constructor.</param>
		public bool WriteUnicodeString(string s, int offsetBytes = 0)
		{
			if(Mem == default) return false;
			if(s.NE()) return true;
			fixed (char* p = s) {
				return Api.WriteProcessMemory(_HprocHR, Mem + offsetBytes, p, (s.Length + 1) * 2, null);
			}
		}

		/// <summary>
		/// Copies a string from this process to the memory allocated in that process by the constructor.
		/// In that process the string is written as '\0'-terminated ANSI string, in default or specified encoding.
		/// Returns false if fails.
		/// </summary>
		/// <param name="s">A string in this process. Normal C# string (UTF-16), not ANSI.</param>
		/// <param name="offsetBytes">Offset in the memory allocated by the constructor.</param>
		/// <param name="enc">If null, uses system's default ANSI encoding.</param>
		public bool WriteAnsiString(string s, int offsetBytes = 0, Encoding enc = null)
		{
			if(Mem == default) return false;
			if(s.NE()) return true;
			if(enc == null) enc = Encoding.Default;
			var a = enc.GetBytes(s + "\0");
			fixed (byte* p = a) {
				return Api.WriteProcessMemory(_HprocHR, Mem + offsetBytes, p, a.Length, null);
			}
		}

		string _ReadString(bool ansiString, int nChars, int offsetBytes, bool findLength, Encoding enc = null)
		{
			if(Mem == default) return null;
			int na = nChars; if(!ansiString) na *= 2;
			var b = Util.AMemoryArray.Char_((na + 1) / 2);
			fixed (char* p = b.A) {
				if(!Api.ReadProcessMemory(_HprocHR, Mem + offsetBytes, p, na, null)) return null;
				if(findLength) {
					if(ansiString) nChars = Util.BytePtr_.Length((byte*)p, nChars);
					else nChars = Util.CharPtr_.Length(p, nChars);
				}
			}
			if(ansiString) return b.ToStringFromAnsi_(nChars, enc);
			return b.ToString(nChars);
		}

		/// <summary>
		/// Copies a string from the memory in that process allocated by the constructor to this process.
		/// Returns the copied string, or null if fails.
		/// In that process the string must be in Unicode UTF-16 format (ie not ANSI).
		/// </summary>
		/// <param name="nChars">Number of characters to copy. In both processes a character is 2 bytes.</param>
		/// <param name="offsetBytes">Offset in the memory allocated by the constructor.</param>
		/// <param name="findLength">Find true string length by searching for '\0' character in nChars range. If false, the returned string is of nChars length even if contains '\0' characters.</param>
		public string ReadUnicodeString(int nChars, int offsetBytes = 0, bool findLength = false)
		{
			return _ReadString(false, nChars, offsetBytes, findLength);
		}

		/// <summary>
		/// Copies a string from the memory in that process allocated by the constructor to this process.
		/// Returns the copies string, or null if fails.
		/// In that process the string must be in ANSI format (ie not Unicode UTF-16).
		/// </summary>
		/// <param name="nBytes">Number bytes to copy. In that process a character is 1 or more bytes (depending on encoding). In this process will be 2 bytes (normal C# string).</param>
		/// <param name="offsetBytes">Offset in the memory allocated by the constructor.</param>
		/// <param name="findLength">Find true string length by searching for '\0' character in nBytes range of the ANSI string.</param>
		/// <param name="enc">If null, uses system's default ANSI encoding.</param>
		public string ReadAnsiString(int nBytes, int offsetBytes = 0, bool findLength = false, Encoding enc = null)
		{
			return _ReadString(true, nBytes, offsetBytes, findLength, enc);
		}

		/// <summary>
		/// Copies a value-type variable or other memory from this process to the memory in that process allocated by the constructor.
		/// Returns false if fails.
		/// </summary>
		/// <param name="ptr">Unsafe address of a value type variable or other memory in this process.</param>
		/// <param name="nBytes">Number of bytes to copy.</param>
		/// <param name="offsetBytes">Offset in the memory allocated by the constructor.</param>
		public bool Write(void* ptr, int nBytes, int offsetBytes = 0)
		{
			if(Mem == default) return false;
			return Api.WriteProcessMemory(_HprocHR, Mem + offsetBytes, ptr, nBytes, null);
		}

		/// <summary>
		/// Copies a value-type variable or other memory from this process to a known memory address in that process.
		/// Returns false if fails.
		/// </summary>
		/// <param name="ptrDestinationInThatProcess">Memory address in that process where to copy memory from this process.</param>
		/// <param name="ptr">Unsafe address of a value type variable or other memory in this process.</param>
		/// <param name="nBytes">Number of bytes to copy.</param>
		/// <seealso cref="SetMem"/>
		public bool WriteOther(IntPtr ptrDestinationInThatProcess, void* ptr, int nBytes)
		{
			return Api.WriteProcessMemory(_HprocHR, ptrDestinationInThatProcess, ptr, nBytes, null);
		}

		/// <summary>
		/// Copies from the memory in that process allocated by the constructor to a value-type variable or other memory in this process.
		/// Returns false if fails.
		/// </summary>
		/// <param name="ptr">Unsafe address of a value type variable or other memory in this process.</param>
		/// <param name="nBytes">Number of bytes to copy.</param>
		/// <param name="offsetBytes">Offset in the memory allocated by the constructor.</param>
		public bool Read(void* ptr, int nBytes, int offsetBytes = 0)
		{
			if(Mem == default) return false;
			return Api.ReadProcessMemory(_HprocHR, Mem + offsetBytes, ptr, nBytes, null);
		}

		/// <summary>
		/// Copies from a known memory address in that process to a value-type variable or other memory in this process.
		/// Returns false if fails.
		/// </summary>
		/// <param name="ptrSourceInThatProcess">Memory address in that process from where to copy memory.</param>
		/// <param name="ptr">Unsafe address of a value type variable or other memory in this process.</param>
		/// <param name="nBytes">Number of bytes to copy.</param>
		/// <seealso cref="SetMem"/>
		public bool ReadOther(IntPtr ptrSourceInThatProcess, void* ptr, int nBytes)
		{
			return Api.ReadProcessMemory(_HprocHR, ptrSourceInThatProcess, ptr, nBytes, null);
		}
	}
}
