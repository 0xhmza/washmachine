import logging
import os
import re
import io
import struct
import operator
import pefile
import collections
import tempfile
import subprocess
logger = logging.getLogger(__name__)


class core:

    def __init__(self, incoming):
        logger.debug("In file {0}\n".format(str(os.path.abspath(__file__))))
        self.core = incoming

    def get_magic(self):
        # This will need to return self.PAYLOAD_PATH_w_CHIP, self.PAYLOAD_PATH_CORE
        # based on e_machine, EI_CLASS, and EI_OSABI
        self.core.bintype = False
        # make patch method parity for ET_EXEC and ET_DYN
        # maybe there will some consolidation with more testing... but for now... different paths
        # maybe symlinks?
        if self.core.elf_object['e_type'] == 0x02:
            self.core.type_path = 'ET_EXEC/'
        elif self.core.elf_object['e_type'] == 0x03:
            self.core.type_path = 'ET_DYN/'

        if self.core.elf_object['e_machine'] == 0x03:  # x86 chipset
            if self.core.elf_object['EI_CLASS'] == 0x1:

                if self.core.elf_object['EI_OSABI'] in [0x00, 0x03]:

                    #self.core.elf_object.bintype = linux_elfI32_shellcode
                    self.core.PAYLOAD_PATH_w_CHIP = self.core.PAYLOAD_PATH + self.core.type_path + 'intel/linux/x86/'
                    self.core.PAYLOAD_PATH_CORE = self.core.PAYLOAD_PATH + self.core.type_path + 'intel/linux/x86/x86_core/'
                    return True

                elif self.core.elf_object['EI_OSABI'] == 0x09 or self.core.elf_object['EI_OSABI'] == 0x0C:

                    #self.bintype = freebsd_elfI32_shellcode
                    self.core.PAYLOAD_PATH_w_CHIP = self.core.PAYLOAD_PATH + self.core.type_path + 'intel/freebsd/x86/'
                    self.core.PAYLOAD_PATH_CORE = self.core.PAYLOAD_PATH + self.core.type_path + 'intel/freebsd/x86/x86_core/'
                    return True

        elif self.core.elf_object['e_machine'] == 0x3E:  # x86-64 chipset
            if self.core.elf_object['EI_CLASS'] == 0x2:
                if self.core.elf_object['EI_OSABI'] in [0x00, 0x03]:
                    #self.core.elf_object.bintype = linux_elfI64_shellcode
                    self.core.PAYLOAD_PATH_w_CHIP = self.core.PAYLOAD_PATH + self.core.type_path + 'intel/linux/x64/'
                    self.core.PAYLOAD_PATH_CORE = self.core.PAYLOAD_PATH + self.core.type_path + 'intel/linux/x64/x64_core/'
                    return True

                #elif self.core.elf_object['EI_OSABI'] == 0x03:
                #    self.core.elf_object.bintype = linux_elfI64_shellcode
                #    return True
                #elif self.core.elf_object['EI_OSABI'] == 0x09:
                #    self.bintype = freebsd_elfI64_shellcode

        elif self.core.elf_object['e_machine'] == 0x28:  # ARM chipset
            if self.core.elf_object['EI_CLASS'] == 0x1:
                if self.core.elf_object['EI_OSABI'] == 0x00:
                    #self.core.elf_object.bintype = linux_elfarmle32_shellcode
                    self.core.PAYLOAD_PATH_w_CHIP = self.core.PAYLOAD_PATH + self.core.type_path + 'arm/linux/32/'
                    self.core.PAYLOAD_PATH_CORE = self.core.PAYLOAD_PATH + self.core.type_path + 'arm/linux/32/32_core/'
                    return True

        return False

