using System.Runtime.InteropServices;
using System.Text;
using System.Linq;

namespace AssemblyIDE
{
    public class Program
    {
        #region ShowDialogueBox Stuff
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct OpenFileName
        {
            public int lStructSize;
            public IntPtr hwndOwner;
            public IntPtr hInstance;
            public string lpstrFilter;
            public string lpstrCustomFilter;
            public int nMaxCustFilter;
            public int nFilterIndex;
            [MarshalAs(UnmanagedType.LPTStr)]
            public string lpstrFile;
            public int nMaxFile;
            [MarshalAs(UnmanagedType.LPTStr)]
            public string lpstrFileTitle;
            public int nMaxFileTitle;
            public string lpstrInitialDir;
            public string lpstrTitle;
            public int Flags;
            public short nFileOffset;
            public short nFileExtension;
            public string lpstrDefExt;
            public IntPtr lCustData;
            public IntPtr lpfnHook;
            public string lpTemplateName;
            public IntPtr pvReserved;
            public int dwReserved;
            public int flagsEx;
        }

        [DllImport("comdlg32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool GetOpenFileName(ref OpenFileName ofn);

        [DllImport("comdlg32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool GetSaveFileName(ref OpenFileName ofn);

        [DllImport("comdlg32.dll")]
        private static extern int CommDlgExtendedError();

        private static string DescribeDialogError(int code)
        {
            return code switch
            {
                0x0000 => "No error (dialog was likely cancelled by the user).",
                0x0001 => "CDERR_STRUCTSIZE: lStructSize is wrong for this struct/platform (common 32-bit vs 64-bit mismatch).",
                0x0002 => "CDERR_INITIALIZATION: comdlg32 failed to initialize.",
                0x0003 => "CDERR_NOTEMPLATE",
                0x0005 => "CDERR_LOADSTRFAILURE",
                0x0006 => "CDERR_MEMLOCKFAILURE",
                0x0007 => "CDERR_MEMALLOCFAILURE",
                0x0009 => "CDERR_LOADRESFAILURE",
                0x000A => "CDERR_FINDRESFAILURE",
                0x3002 => "FNERR_INVALIDFILENAME: the filename buffer contains an invalid path/name.",
                0x3003 => "FNERR_BUFFERTOOSMALL: lpstrFile buffer (nMaxFile) is too small for the selected path.",
                0x3001 => "FNERR_SUBCLASSFAILURE",
                _ => $"Unknown/undocumented error code: 0x{code:X}"
            };
        }

        private const string FilterString =
            "Supported Files (*.asm;*.txt;*.obj)\0*.asm;*.txt;*.obj\0" +
            "Assembly File (*.asm)\0*.asm\0" +
            "Text File (*.txt)\0*.txt\0" +
            "Object File (*.obj)\0*.obj\0" +
            "All Files (*.*)\0*.*\0\0";

        private static string ShowDialog()
        {
            var ofn = new OpenFileName();
            ofn.lStructSize = Marshal.SizeOf(ofn);
            ofn.lpstrFilter = FilterString;
            ofn.nFilterIndex = 1;
            ofn.lpstrFile = new string(new char[256]);
            ofn.nMaxFile = ofn.lpstrFile.Length;
            ofn.lpstrFileTitle = new string(new char[64]);
            ofn.nMaxFileTitle = ofn.lpstrFileTitle.Length;
            ofn.lpstrTitle = "Open File Dialog...";
            if (GetOpenFileName(ref ofn))
                return ofn.lpstrFile.Split('\0')[0];

            int err = CommDlgExtendedError();
            if (err != 0)
                Console.WriteLine($"Open dialog failed: {DescribeDialogError(err)}");
            return string.Empty;
        }

        private static string ShowSaveDialog()
        {
            var ofn = new OpenFileName();
            ofn.lStructSize = Marshal.SizeOf(ofn);
            ofn.lpstrFilter = FilterString;
            ofn.nFilterIndex = 1;
            ofn.lpstrFile = new string(new char[256]);
            ofn.nMaxFile = ofn.lpstrFile.Length;
            ofn.lpstrFileTitle = new string(new char[64]);
            ofn.nMaxFileTitle = ofn.lpstrFileTitle.Length;
            ofn.lpstrTitle = "Save File As...";
            ofn.lpstrDefExt = "txt";
            // OFN_OVERWRITEPROMPT = 0x2, OFN_PATHMUSTEXIST = 0x800
            ofn.Flags = 0x2 | 0x800;
            if (GetSaveFileName(ref ofn))
                return ofn.lpstrFile.Split('\0')[0];

            int err = CommDlgExtendedError();
            if (err != 0)
                Console.WriteLine($"Save dialog failed: {DescribeDialogError(err)}");
            return string.Empty;
        }
        #endregion

        public static void Main()
        {
            Console.Title = "Phexon's ASM IDE";
            Console.WriteLine("Hello, World!");
            Console.WriteLine("This program helps you edit and run assembly code");
            while (true)
            {
                Console.WriteLine();
                Console.WriteLine("0. Close program");
                Console.WriteLine("1. New Project");
                Console.WriteLine("2. Open Project");
                Console.WriteLine("3. Open Any File (no project)");

                string input = Console.ReadLine() ?? string.Empty;
                switch (input)
                {
                    case "0":
                        return;
                    case "1":
                        ProjectManager.CreateProject();
                        break;
                    case "2":
                        ProjectManager.OpenProjectMenu();
                        break;
                    case "3":
                        OpenFile();
                        break;
                }
            }
        }

        public static void OpenFile()
        {
            var filename = ShowDialog();
            if (string.IsNullOrEmpty(filename))
                return;
            new TextEditor(filename).Run();
        }

        public static string PromptSavePath() => ShowSaveDialog();
    }

    /// <summary>
    /// Manages assembly "projects": folders on the Desktop named
    /// ASSEMBLY_PROJ_&lt;name&gt;, each containing a single .asm source file.
    /// Handles creating new projects, listing/opening existing ones, and
    /// invoking an external assembler to compile the .asm into an object file.
    /// </summary>
    public static class ProjectManager
    {
        private const string ProjectPrefix = "ASSEMBLY_PROJ_";

        // ASSUMPTION: NASM (https://www.nasm.us/) is the assembler in use,
        // targeting Win64 console programs entered at a `start:` label and
        // linked with link.exe from the VS2022 "x64 Native Tools" toolchain
        // (kernel32.lib + msvcrt.lib), matching the phexon.uk NASM guide.
        private const string AssemblerExecutable = "nasm";
        private const string EntryPointLabel = "start";

        private static readonly string[] KnownNasmPaths =
        {
            @"C:\Program Files\NASM\nasm.exe",
            @"C:\Program Files (x86)\NASM\nasm.exe"
        };

        private static readonly string[] KnownVcVarsPaths =
        {
            @"C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat",
            @"C:\Program Files\Microsoft Visual Studio\2022\Professional\VC\Auxiliary\Build\vcvars64.bat",
            @"C:\Program Files\Microsoft Visual Studio\2022\Enterprise\VC\Auxiliary\Build\vcvars64.bat",
            @"C:\Program Files\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars64.bat"
        };

        private static string ResolveExecutable(string exeName, string[] knownFullPaths)
        {
            string? pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (pathEnv != null)
            {
                foreach (var dir in pathEnv.Split(Path.PathSeparator))
                {
                    try
                    {
                        string candidate = Path.Combine(dir, exeName + ".exe");
                        if (File.Exists(candidate))
                            return candidate;
                    }
                    catch { /* malformed PATH entry, skip */ }
                }
            }

            foreach (var full in knownFullPaths)
                if (File.Exists(full))
                    return full;

            return exeName; // let Process.Start attempt PATH lookup and surface Win32Exception if truly missing
        }

        private static string? FindVcVars64() => Array.Find(KnownVcVarsPaths, File.Exists);

        private static string DesktopPath =>
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

        public static void CreateProject()
        {
            Console.Write("Project name (e.g. MyFirstAssmProject): ");
            string? rawName = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(rawName))
            {
                Console.WriteLine("Project name cannot be empty.");
                return;
            }

            string safeName = SanitizeName(rawName.Trim());
            string folderPath = Path.Combine(DesktopPath, ProjectPrefix + safeName);

            if (Directory.Exists(folderPath))
            {
                Console.WriteLine($"A project already exists at: {folderPath}");
                Console.WriteLine("Opening it instead.");
                OpenProjectFolder(folderPath);
                return;
            }

            try
            {
                Directory.CreateDirectory(folderPath);
                string asmPath = Path.Combine(folderPath, safeName + ".asm");
                File.WriteAllText(asmPath, BuildStarterAsm(safeName));

                Console.WriteLine($"Created project folder: {folderPath}");
                OpenProjectFolder(folderPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to create project: {ex.Message}");
            }
        }

        public static void OpenProjectMenu()
        {
            if (!Directory.Exists(DesktopPath))
            {
                Console.WriteLine("Could not locate the Desktop folder.");
                return;
            }

            var projects = Directory.GetDirectories(DesktopPath, ProjectPrefix + "*")
                .OrderBy(p => p)
                .ToList();

            if (projects.Count == 0)
            {
                Console.WriteLine("No projects found on the Desktop yet. Use 'New Project' to create one.");
                return;
            }

            Console.WriteLine();
            Console.WriteLine("Projects found:");
            for (int i = 0; i < projects.Count; i++)
            {
                string displayName = Path.GetFileName(projects[i])[ProjectPrefix.Length..];
                Console.WriteLine($"{i + 1}. {displayName}");
            }
            Console.WriteLine("0. Back");

            Console.Write("Select a project: ");
            string? choice = Console.ReadLine();
            if (!int.TryParse(choice, out int index) || index == 0)
                return;
            if (index < 1 || index > projects.Count)
            {
                Console.WriteLine("Invalid selection.");
                return;
            }

            OpenProjectFolder(projects[index - 1]);
        }

        private static void OpenProjectFolder(string folderPath)
        {
            while (true)
            {
                string[] asmFiles = Directory.GetFiles(folderPath, "*.asm");

                Console.WriteLine();
                Console.WriteLine($"Project: {Path.GetFileName(folderPath)}");
                Console.WriteLine("0. Back");
                Console.WriteLine("1. Edit .asm");
                Console.WriteLine("2. Compile (assemble + link)");
                Console.WriteLine("3. Run .exe");

                Console.Write("Choice: ");
                string? choice = Console.ReadLine();

                if (choice == "0" || string.IsNullOrEmpty(choice))
                    return;

                if (asmFiles.Length == 0)
                {
                    Console.WriteLine("No .asm file found in this project folder.");
                    continue;
                }

                string asmPath = asmFiles[0];
                if (asmFiles.Length > 1)
                    Console.WriteLine($"Note: multiple .asm files found, using {Path.GetFileName(asmPath)}.");

                switch (choice)
                {
                    case "1":
                        new TextEditor(asmPath).Run();
                        break;
                    case "2":
                        Compile(asmPath);
                        break;
                    case "3":
                        RunExe(asmPath);
                        break;
                }
            }
        }

        private static string DetectEntryPoint(string asmPath, string fallback)
        {
            try
            {
                foreach (var rawLine in File.ReadLines(asmPath))
                {
                    string line = rawLine.Trim();
                    int semi = line.IndexOf(';');
                    if (semi >= 0)
                        line = line[..semi].Trim();

                    if (line.StartsWith("global", StringComparison.OrdinalIgnoreCase))
                    {
                        string rest = line["global".Length..].Trim();
                        if (rest.Length > 0)
                        {
                            string symbol = rest.Split(new[] { ' ', '\t', ',' },
                                StringSplitOptions.RemoveEmptyEntries)[0];
                            if (symbol.Length > 0)
                                return symbol; // preserve exact case from source
                        }
                    }
                }
            }
            catch { /* fall through to fallback */ }
            return fallback;
        }

        // Heuristic: if the source references user32 window-management APIs,
        // treat it as a GUI program (needs user32.lib + /subsystem:windows)
        // rather than the plain console pipeline (kernel32.lib msvcrt.lib).
        private static readonly string[] GuiApiMarkers =
        {
            "CreateWindowExA", "CreateWindowW", "RegisterClassExA", "RegisterClassA", "WinMain"
        };

        private static bool LooksLikeGuiProgram(string asmPath)
        {
            try
            {
                string text = File.ReadAllText(asmPath);
                foreach (var marker in GuiApiMarkers)
                    if (text.Contains(marker, StringComparison.OrdinalIgnoreCase))
                        return true;
            }
            catch { /* assume console on read failure */ }
            return false;
        }

        private static void Compile(string asmPath)
        {
            string folder = Path.GetDirectoryName(asmPath) ?? DesktopPath;
            string objPath = Path.ChangeExtension(asmPath, ".obj");
            string exePath = Path.ChangeExtension(asmPath, ".exe");

            string? vcvars = FindVcVars64();
            if (vcvars == null)
            {
                Console.WriteLine("Could not find vcvars64.bat for Visual Studio 2022.");
                Console.WriteLine("Install the \"Desktop development with C++\" workload for VS2022");
                Console.WriteLine("(it provides link.exe and kernel32.lib/msvcrt.lib), or add its path");
                Console.WriteLine("to KnownVcVarsPaths in ProjectManager.");
                return;
            }

            string nasmPath = ResolveExecutable(AssemblerExecutable, KnownNasmPaths);
            string entryPoint = DetectEntryPoint(asmPath, EntryPointLabel);
            bool isGui = LooksLikeGuiProgram(asmPath);
            string subsystem = isGui ? "windows" : "console";
            string libs = isGui ? "user32.lib kernel32.lib" : "kernel32.lib msvcrt.lib";

            Console.WriteLine($"Detected {(isGui ? "GUI (Windows subsystem)" : "console")} program, entry point '{entryPoint}'.");

            // Chain everything through cmd.exe so vcvars64.bat's environment
            // (PATH/LIB/INCLUDE for link.exe) carries through to the link step.
            string chained =
                $"call \"{vcvars}\" >nul && " +
                $"\"{nasmPath}\" -f win64 \"{asmPath}\" -o \"{objPath}\" && " +
                $"link /subsystem:{subsystem} /entry:{entryPoint} \"{objPath}\" {libs} /out:\"{exePath}\"";

            Console.WriteLine("Assembling and linking...");

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{chained}\"",
                WorkingDirectory = folder,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            try
            {
                using var process = System.Diagnostics.Process.Start(psi);
                if (process == null)
                {
                    Console.WriteLine("Failed to start the build process.");
                    return;
                }

                string stdout = process.StandardOutput.ReadToEnd();
                string stderr = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (!string.IsNullOrWhiteSpace(stdout))
                    Console.WriteLine(stdout);
                if (!string.IsNullOrWhiteSpace(stderr))
                    Console.WriteLine(stderr);

                Console.WriteLine(process.ExitCode == 0 && File.Exists(exePath)
                    ? $"Build succeeded: {exePath}"
                    : $"Build failed (exit code {process.ExitCode}). Check the assembler/linker output above.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Build error: {ex.Message}");
            }
        }

        private static void RunExe(string asmPath)
        {
            string exePath = Path.ChangeExtension(asmPath, ".exe");
            string folder = Path.GetDirectoryName(asmPath) ?? DesktopPath;

            if (!File.Exists(exePath))
            {
                Console.WriteLine("No .exe found yet — compile the project first.");
                return;
            }

            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = exePath,
                    WorkingDirectory = folder,
                    UseShellExecute = true // opens its own console window, like double-clicking the .exe
                };
                System.Diagnostics.Process.Start(psi);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Could not run {exePath}: {ex.Message}");
            }
        }

        private static string SanitizeName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name.Replace(' ', '_');
        }

        private static string BuildStarterAsm(string projectName)
        {
            string nl = Environment.NewLine;
            return
                $"; {projectName}.asm{nl}" +
                $"; NASM x64 program for Windows.{nl}" +
                $"; Prints a message, waits for a keypress, then exits.{nl}" +
                $"default rel{nl}" +
                $"global {EntryPointLabel}{nl}{nl}" +
                $"; Windows API functions we call from kernel32.dll{nl}" +
                $"extern GetStdHandle{nl}" +
                $"extern WriteConsoleA{nl}" +
                $"extern ReadConsoleA{nl}" +
                $"extern ExitProcess{nl}{nl}" +
                $"section .data{nl}" +
                $"    outbuf db 'Hello from {projectName}!', 13, 10{nl}" +
                $"    outlen equ $ - outbuf   ; length of outbuf in bytes{nl}" +
                $"    written dq 0            ; number of chars actually written{nl}{nl}" +
                $"    ; Input buffer used only to pause/wait for a key{nl}" +
                $"    inbuf db 0{nl}" +
                $"    readcount dq 0          ; number of chars read{nl}{nl}" +
                $"section .text{nl}" +
                $"{EntryPointLabel}:{nl}" +
                $"    ; Reserve 40 bytes on stack:{nl}" +
                $"    ; - required shadow space for the Win64 calling convention{nl}" +
                $"    ; - keeps the stack aligned before API calls{nl}" +
                $"    sub rsp, 40{nl}{nl}" +
                $"    ; Get handle for standard output (console output){nl}" +
                $"    ; STD_OUTPUT_HANDLE = -11{nl}" +
                $"    mov ecx, -11{nl}" +
                $"    call GetStdHandle       ; handle returned in RAX{nl}{nl}" +
                $"    ; WriteConsoleA(stdout, outbuf, outlen, &written, 0){nl}" +
                $"    mov rcx, rax            ; 1st arg: console output handle{nl}" +
                $"    lea rdx, [outbuf]       ; 2nd arg: pointer to text{nl}" +
                $"    mov r8d, outlen         ; 3rd arg: number of chars to write{nl}" +
                $"    lea r9, [written]       ; 4th arg: where to store chars written{nl}" +
                $"    mov qword [rsp+32], 0   ; 5th arg (on stack): reserved = NULL{nl}" +
                $"    call WriteConsoleA{nl}{nl}" +
                $"    ; Get handle for standard input (keyboard){nl}" +
                $"    ; STD_INPUT_HANDLE = -10{nl}" +
                $"    mov ecx, -10{nl}" +
                $"    call GetStdHandle       ; handle returned in RAX{nl}{nl}" +
                $"    ; ReadConsoleA(stdin, inbuf, 1, &readcount, 0){nl}" +
                $"    ; This waits for input so the window doesn't close immediately.{nl}" +
                $"    mov rcx, rax            ; 1st arg: console input handle{nl}" +
                $"    lea rdx, [inbuf]        ; 2nd arg: input buffer{nl}" +
                $"    mov r8d, 1              ; 3rd arg: read 1 character{nl}" +
                $"    lea r9, [readcount]     ; 4th arg: chars read{nl}" +
                $"    mov qword [rsp+32], 0   ; 5th arg (on stack): reserved = NULL{nl}" +
                $"    call ReadConsoleA{nl}{nl}" +
                $"    ; ExitProcess(0) -> exit code 0 means success{nl}" +
                $"    xor ecx, ecx{nl}" +
                $"    call ExitProcess{nl}";
        }
    }

    /// <summary>
    /// A minimal nano-style text editor that runs directly in the console.
    /// Arrow keys move the cursor, typing inserts text, Enter/Backspace/Delete
    /// edit lines, F2 saves, and Ctrl+X (or Esc) exits.
    /// </summary>
    public class TextEditor
    {
        private List<string> _lines;
        private string? _filePath;
        private int _cursorX;
        private int _cursorY;
        private int _topLine;
        private bool _dirty;
        private string? _statusMessage;

        private const int HeaderRows = 2;
        private const int FooterRows = 1;

        public TextEditor(string? filePath)
        {
            _filePath = filePath;
            _lines = new List<string> { string.Empty };

            if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
            {
                var text = File.ReadAllText(filePath);
                _lines = new List<string>(text.Replace("\r\n", "\n").Split('\n'));
                if (_lines.Count == 0)
                    _lines.Add(string.Empty);
            }
        }

        public void Run()
        {
            Console.CursorVisible = true;
            bool running = true;

            while (running)
            {
                Render();
                var key = Console.ReadKey(true);
                running = HandleKey(key);
            }

            Console.Clear();
            Console.CursorVisible = true;
        }

        private bool HandleKey(ConsoleKeyInfo key)
        {
            bool ctrl = (key.Modifiers & ConsoleModifiers.Control) != 0;

            if (key.Key == ConsoleKey.F2 || (ctrl && key.Key == ConsoleKey.S))
            {
                Save();
                return true;
            }

            // Any other key clears the transient status message (e.g. "Saved" / error text)
            _statusMessage = null;

            if ((ctrl && key.Key == ConsoleKey.X) || key.Key == ConsoleKey.Escape)
            {
                return !ConfirmExit();
            }

            switch (key.Key)
            {
                case ConsoleKey.UpArrow:
                    if (_cursorY > 0)
                    {
                        _cursorY--;
                        _cursorX = Math.Min(_cursorX, _lines[_cursorY].Length);
                    }
                    break;

                case ConsoleKey.DownArrow:
                    if (_cursorY < _lines.Count - 1)
                    {
                        _cursorY++;
                        _cursorX = Math.Min(_cursorX, _lines[_cursorY].Length);
                    }
                    break;

                case ConsoleKey.LeftArrow:
                    if (_cursorX > 0)
                    {
                        _cursorX--;
                    }
                    else if (_cursorY > 0)
                    {
                        _cursorY--;
                        _cursorX = _lines[_cursorY].Length;
                    }
                    break;

                case ConsoleKey.RightArrow:
                    if (_cursorX < _lines[_cursorY].Length)
                    {
                        _cursorX++;
                    }
                    else if (_cursorY < _lines.Count - 1)
                    {
                        _cursorY++;
                        _cursorX = 0;
                    }
                    break;

                case ConsoleKey.Home:
                    _cursorX = 0;
                    break;

                case ConsoleKey.End:
                    _cursorX = _lines[_cursorY].Length;
                    break;

                case ConsoleKey.PageUp:
                    _cursorY = Math.Max(0, _cursorY - AvailableRows());
                    _cursorX = Math.Min(_cursorX, _lines[_cursorY].Length);
                    break;

                case ConsoleKey.PageDown:
                    _cursorY = Math.Min(_lines.Count - 1, _cursorY + AvailableRows());
                    _cursorX = Math.Min(_cursorX, _lines[_cursorY].Length);
                    break;

                case ConsoleKey.Enter:
                    {
                        var current = _lines[_cursorY];
                        var tail = current.Substring(_cursorX);
                        _lines[_cursorY] = current.Substring(0, _cursorX);
                        _lines.Insert(_cursorY + 1, tail);
                        _cursorY++;
                        _cursorX = 0;
                        _dirty = true;
                    }
                    break;

                case ConsoleKey.Backspace:
                    if (_cursorX > 0)
                    {
                        _lines[_cursorY] = _lines[_cursorY].Remove(_cursorX - 1, 1);
                        _cursorX--;
                        _dirty = true;
                    }
                    else if (_cursorY > 0)
                    {
                        int prevLen = _lines[_cursorY - 1].Length;
                        _lines[_cursorY - 1] += _lines[_cursorY];
                        _lines.RemoveAt(_cursorY);
                        _cursorY--;
                        _cursorX = prevLen;
                        _dirty = true;
                    }
                    break;

                case ConsoleKey.Delete:
                    if (_cursorX < _lines[_cursorY].Length)
                    {
                        _lines[_cursorY] = _lines[_cursorY].Remove(_cursorX, 1);
                        _dirty = true;
                    }
                    else if (_cursorY < _lines.Count - 1)
                    {
                        _lines[_cursorY] += _lines[_cursorY + 1];
                        _lines.RemoveAt(_cursorY + 1);
                        _dirty = true;
                    }
                    break;

                default:
                    if (!char.IsControl(key.KeyChar))
                    {
                        _lines[_cursorY] = _lines[_cursorY].Insert(_cursorX, key.KeyChar.ToString());
                        _cursorX++;
                        _dirty = true;
                    }
                    break;
            }

            return true;
        }

        private bool ConfirmExit()
        {
            if (!_dirty)
                return true;

            Console.SetCursorPosition(0, Console.WindowHeight - 1);
            Console.Write(new string(' ', Console.WindowWidth - 1));
            Console.SetCursorPosition(0, Console.WindowHeight - 1);
            Console.Write("Save changes before exiting? (y/n/c=cancel): ");
            var response = Console.ReadKey(true).KeyChar;

            if (response == 'y' || response == 'Y')
            {
                Save();
                return true;
            }
            if (response == 'n' || response == 'N')
            {
                return true;
            }
            return false; // cancel: stay in editor
        }

        private void Save()
        {
            if (string.IsNullOrEmpty(_filePath))
            {
                var chosen = Program.PromptSavePath();
                if (string.IsNullOrEmpty(chosen))
                {
                    _statusMessage = "Save cancelled.";
                    return;
                }
                _filePath = chosen;
            }

            try
            {
                File.WriteAllLines(_filePath, _lines, new UTF8Encoding(false));
                _dirty = false;
                _statusMessage = $"Saved {_lines.Count} line(s) to {_filePath}";
            }
            catch (Exception ex)
            {
                _statusMessage = $"SAVE FAILED: {ex.Message}";
            }
        }

        private int AvailableRows() => Math.Max(1, Console.WindowHeight - HeaderRows - FooterRows);

        private void Render()
        {
            Console.SetCursorPosition(0, 0);
            Console.CursorVisible = false;

            int width = Math.Max(10, Console.WindowWidth);
            int rows = AvailableRows();

            // Keep cursor within the visible viewport
            if (_cursorY < _topLine)
                _topLine = _cursorY;
            if (_cursorY >= _topLine + rows)
                _topLine = _cursorY - rows + 1;

            string title = string.IsNullOrEmpty(_filePath) ? "[New File]" : Path.GetFileName(_filePath);
            string header = $" {title}{(_dirty ? " *" : "")}";
            WriteFullLine(header.PadRight(width));
            WriteFullLine("F2 Save   Ctrl+X / Esc Exit   Arrows Move".PadRight(width));

            for (int row = 0; row < rows; row++)
            {
                int lineIndex = _topLine + row;
                if (lineIndex < _lines.Count)
                {
                    string lineNum = (lineIndex + 1).ToString().PadLeft(4);
                    string content = _lines[lineIndex];
                    string display = $"{lineNum}| {content}";
                    if (display.Length > width)
                        display = display.Substring(0, width);
                    WriteFullLine(display.PadRight(width));
                }
                else
                {
                    WriteFullLine("~".PadRight(width));
                }
            }

            string status = _statusMessage ?? $" Line {_cursorY + 1}, Col {_cursorX + 1}";
            WriteFullLine(status.PadRight(width));

            int screenRow = HeaderRows + (_cursorY - _topLine);
            int screenCol = Math.Min(width - 1, _cursorX + 6); // +6 for "NNNN| " prefix
            Console.SetCursorPosition(screenCol, screenRow);
            Console.CursorVisible = true;
        }

        private void WriteFullLine(string text)
        {
            Console.Write(text);
        }
    }
}