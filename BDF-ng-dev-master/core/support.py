import os
import sys
import struct
from core import enum
import logging
import collections

logger = logging.getLogger(__name__)


class support():
    '''
    Checks requirements for modes and payloads/shells, called by patching methods
    takes a PatchingMethod class object.
    NOTE: Each file format needs its own support check due to differences
    TODO: move this to under pe/
    '''

    def __init__(self, PM):
        logger.debug("In file {0}".format(str(os.path.abspath(__file__))))

        self.PM = PM

    def check_reqs(self):
        check = True
        self.PM.found_mode = False
        self.PM.found_payload = False
        self.PM.found_core = False
        self.PM.found_modifier = False
        self.PM.found_enccoder = False

        for key, value in self.PM.requirements.items():
            if key not in self.PM.BDF.options.keys() or self.PM.BDF.options[key] == '':
                logger.error("Missing %s=%s", key, value)
                check = False
                if "PAYLOAD" in key:
                    self.check_payloads() 

                if "MODE" in key:
                     self.check_modes()

                if "ENCODER" in key:
                    self.check_encoders()
                # add method for PAYLOAD, MODE Paths for 
                #   finding and printing what's missing
            else:
                setattr(self.PM, key, self.PM.BDF.options[key])

        if check is False:  
            return False

        return True

    def print_methods(self):
        logger.error("Need a PATCH_METHOD, here's what is available:\n")
        for patch_method in self.PM.patch_methods:
            # TODO: Clean up
            logger.info("Method: {0}".format(patch_method.__class__.__name__))
            logger.info("Description: {0}".format(patch_method.description))
            logger.info("Requirements: {0}".format(patch_method.requirements))
            #logger.info("Supported Modes: {0}".format(patch_method.supported_modes))
            #logger.info("Supported Payloads: {0}".format(patch_method.supported_payloads))
            logger.info("=======")

    def check_modifier(self):
        logger.debug(f"Modifier: {self.PM.BDF.options['MODIFIER'].lower()}")
        if 'manual' not in self.PM.BDF.options['MODIFIER'].lower() and 'automatic' not in self.PM.BDF.options['MODIFIER'].lower():
            logger.error("Pick a proper modifier, hint: manual or automatic")
            return False

        return True

    def check_encoders(self):
        logger.debug("Check encoders")
        self.PM.supported_encoders = enum.enum(self.PM.BDF.ENCODER_PATH).run()

        if not self.PM.ENCODER:
            logger.debug("No encoder selected, payload will not be encoded")
            self.PM.found_encoder = False
            return True

        def print_encoders():
            print("You must pick an ENCODER to use: ENCODER=<encoder>")
            for supported_encoder in self.PM.supported_encoders:
                if self.PM.found_mode.name in supported_encoder.__class__().supported_modes:
                    print("\t{0}".format(supported_encoder.__class__.__name__.lower()))

        if "ENCODER" not in self.PM.BDF.options.keys():
            print_encoders()
            return False
        else:
            for supported_encoder in self.PM.supported_encoders:
                if self.PM.ENCODER.lower() == supported_encoder.__class__.__name__.lower():
                    self.PM.found_encoder = supported_encoder
                    break

        if self.PM.found_encoder:
            return True
        else:
            print_encoders()
            return False

    def print_modes(self):
        print("You must choose a MODE to use: MODE=<your mode>")
        for supported_mode in self.PM.supported_modes:
            if self.PM.name.lower() in supported_mode.__class__().supported_methods:
                print("\t{0}".format(supported_mode.__class__.__name__.lower()))

    def check_modes(self):
        # put modes in a check function
        logger.debug("Check_modes")
        self.PM.supported_modes = enum.enum(self.PM.BDF.MODE_PATH).run()

        if 'MODE' not in self.PM.BDF.options.keys():
            self.print_modes()
            return False
        else:
            for supported_mode in self.PM.supported_modes:
                if self.PM.MODE.lower() == supported_mode.__class__.__name__.lower() and self.PM.name.lower() in supported_mode.__class__().supported_methods:

                    self.PM.found_mode = supported_mode
                    break

        if self.PM.found_mode:
            return True
        else:
            self.print_modes()
            return False

    def print_payloads(self):
        print("You must choose a PAYLOAD to use: PAYLOAD=<your payload>")
        for item in self.PM.supported_payloads:
            for mode in item.__class__().supported_modes:
                if not self.PM.found_mode:
                    return 
                elif mode == self.PM.found_mode.name:
                    print("   {0}".format(item.__class__.__name__.lower()))

    def check_payloads(self):
        logger.debug(f"self.PM.BDF.PAYLOAD_PATH: {self.PM.BDF.PAYLOAD_PATH}")
        # You could just call check_chips here
        logger.debug(f"PAYLOAD_PATH_w_CHIP: {self.PM.BDF.PAYLOAD_PATH_w_CHIP}")

        self.PM.supported_payloads = enum.enum(self.PM.BDF.PAYLOAD_PATH_w_CHIP).run()
        logger.debug(f"supported_payloads: {self.PM.supported_payloads}")

        if 'PAYLOAD' not in self.PM.BDF.options.keys():
            self.print_payloads()
            return False
        elif self.PM.BDF.options['PAYLOAD'] == '':
            self.print_payloads()
            return False
        else:
            for aPayload in self.PM.supported_payloads:
                logger.debug(f'aPayload: {type(aPayload)}')
                if self.PM.PAYLOAD.lower() == aPayload.__class__.__name__.lower():
                    self.PM.found_payload = aPayload
                    break

        if self.PM.found_payload:
            for supported_mode in self.PM.found_payload.supported_modes:
                if supported_mode == self.PM.found_mode.name:
                    return True

            logger.error(f'{self.PM.found_mode.name} MODE does not support {self.PM.found_payload.name}')
            self.print_payloads()
            return False

        else:
            self.print_payloads()
            return False

    def check_core(self):
        logger.debug("check core")
        self.PM.supported_cores = enum.enum(self.PM.BDF.PAYLOAD_PATH_CORE).run()

        logger.debug(f"self.PM.supported_cores: {self.PM.supported_cores}")
        logger.debug(f"self.PM.BDF.core_support: {self.PM.BDF.core_support}")

        if self.PM.supported_cores == []:
            logger.error("[!] Your method core is missing... you need to make one")
            return False

        if not hasattr(self.PM.BDF, 'core_support'):
            logger.error("[!] You must set self.BDF.core_support in your methods file")
            return False
        else:
            for aCore in self.PM.supported_cores:
                if self.PM.BDF.core_support.lower() == aCore.__class__.__name__.lower():
                    self.PM.found_core = aCore
                    break
        if self.PM.found_core:
            return True
        else:
            logger.error("[!] You must set self.BDF.options['core_support'] in your methods file")
            return False

    def patch_file(self):
        '''
        This function takes patch_instr and patches a file
        '''
        logger.debug(self.PM.BDF.patch_instr)
        logger.info('[*] Patching file with patch_instr')
        tmp = b''
        for addr, values in self.PM.BDF.patch_instr.items():
            # Add COPY instruction    
            if addr == 'filehash':
                continue
            elif type(addr) == type(int()):
                self.PM.patched_binary.seek(addr,0)
                self.PM.patched_binary.write(values)
            elif addr == 'truncate':
                self.PM.patched_binary.seek(values)
                self.PM.patched_binary.truncate()
            elif 'COPY' in addr:
                self.PM.patched_binary.seek(values[0],0)
                tmp = self.PM.patched_binary.read(values[1])
                self.PM.patched_binary.seek(0)
            elif 'PASTE' in addr:
                self.PM.patched_binary.seek(values,0)
                self.PM.patched_binary.write(tmp)
                self.PM.patched_binary.seek(0)
            else:
                # This is where the addr is a word 
                # and it continues the last addr
                self.PM.patched_binary.write(values)
