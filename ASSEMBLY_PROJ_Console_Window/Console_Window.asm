; Console_Window.asm
; NASM x64 program for Windows.
; Prints a message, waits for a keypress, then exits.
default rel
global start

; Windows API functions we call from kernel32.dll
extern GetStdHandle
extern WriteConsoleA
extern ReadConsoleA
extern ExitProcess

section .data
    outbuf db 'Hello from Console_Window!', 13, 10
    outlen equ $ - outbuf   ; length of outbuf in bytes
    written dq 0            ; number of chars actually written

    ; Input buffer used only to pause/wait for a key
    inbuf db 0
    readcount dq 0          ; number of chars read

section .text
start:
    ; Reserve 40 bytes on stack:
    ; - required shadow space for the Win64 calling convention
    ; - keeps the stack aligned before API calls
    sub rsp, 40

    ; Get handle for standard output (console output)
    ; STD_OUTPUT_HANDLE = -11
    mov ecx, -11
    call GetStdHandle       ; handle returned in RAX

    ; WriteConsoleA(stdout, outbuf, outlen, &written, 0)
    mov rcx, rax            ; 1st arg: console output handle
    lea rdx, [outbuf]       ; 2nd arg: pointer to text
    mov r8d, outlen         ; 3rd arg: number of chars to write
    lea r9, [written]       ; 4th arg: where to store chars written
    mov qword [rsp+32], 0   ; 5th arg (on stack): reserved = NULL
    call WriteConsoleA

    ; Get handle for standard input (keyboard)
    ; STD_INPUT_HANDLE = -10
    mov ecx, -10
    call GetStdHandle       ; handle returned in RAX

    ; ReadConsoleA(stdin, inbuf, 1, &readcount, 0)
    ; This waits for input so the window doesn't close immediately.
    mov rcx, rax            ; 1st arg: console input handle
    lea rdx, [inbuf]        ; 2nd arg: input buffer
    mov r8d, 1              ; 3rd arg: read 1 character
    lea r9, [readcount]     ; 4th arg: chars read
    mov qword [rsp+32], 0   ; 5th arg (on stack): reserved = NULL
    call ReadConsoleA

    ; ExitProcess(0) -> exit code 0 means success
    xor ecx, ecx
    call ExitProcess

