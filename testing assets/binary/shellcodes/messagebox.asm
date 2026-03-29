; Build with:
;   nasm -f bin -o messagebox.bin messagebox.asm

bits 64
default rel

start:
    sub rsp, 0x28

    xor rdx, rdx
    mov rdx, [gs:rdx + 0x60]
    mov rdx, [rdx + 0x18]
    mov rdx, [rdx + 0x20]

find_kernel32:
    mov rsi, [rdx + 0x50]
    cmp word [rsi + 0x00], 'K'
    jne next_module
    cmp word [rsi + 0x02], 'E'
    jne next_module
    cmp word [rsi + 0x04], 'R'
    jne next_module
    cmp word [rsi + 0x06], 'N'
    jne next_module
    cmp word [rsi + 0x08], 'E'
    jne next_module
    cmp word [rsi + 0x0A], 'L'
    jne next_module
    cmp word [rsi + 0x0C], '3'
    jne next_module
    cmp word [rsi + 0x0E], '2'
    jne next_module
    mov rbx, [rdx + 0x20]
    jmp have_kernel32

next_module:
    mov rdx, [rdx]
    jmp find_kernel32

have_kernel32:
    mov eax, [rbx + 0x3C]
    add rax, rbx
    mov eax, [rax + 0x88]
    add rax, rbx
    mov rsi, rax
    mov ecx, [rsi + 0x18]
    mov eax, [rsi + 0x20]
    add rax, rbx
    mov rdi, rax
    xor edx, edx

find_getproc:
    mov eax, [rdi + rdx * 4]
    add rax, rbx
    cmp dword [rax + 0x00], 0x50746547
    jne next_name
    cmp dword [rax + 0x04], 0x41636F72
    jne next_name
    cmp dword [rax + 0x08], 0x65726464
    jne next_name
    cmp word [rax + 0x0C], 0x7373
    jne next_name
    cmp byte [rax + 0x0E], 0x00
    jne next_name
    jmp found_getproc

next_name:
    inc edx
    cmp edx, ecx
    jne find_getproc
    jmp done

found_getproc:
    mov eax, [rsi + 0x24]
    add rax, rbx
    movzx edx, word [rax + rdx * 2]
    mov eax, [rsi + 0x1C]
    add rax, rbx
    mov eax, [rax + rdx * 4]
    add rax, rbx
    mov rbp, rax

    mov rcx, rbx
    lea rdx, [rel loadlibrarya_str]
    call rbp
    test rax, rax
    jz done
    mov rbx, rax

    lea rcx, [rel user32_str]
    call rbx
    test rax, rax
    jz done

    mov rcx, rax
    lea rdx, [rel messageboxa_str]
    call rbp
    test rax, rax
    jz done
    mov rbx, rax

    xor ecx, ecx
    lea rdx, [rel text_str]
    lea r8, [rel caption_str]
    xor r9d, r9d
    call rbx

done:
    add rsp, 0x28
    ret

loadlibrarya_str: db 'LoadLibraryA', 0
user32_str: db 'user32.dll', 0
messageboxa_str: db 'MessageBoxA', 0
text_str: db 'Injected!', 0
caption_str: db 'Washmachine', 0
