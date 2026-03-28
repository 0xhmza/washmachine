import collections
import io
from pe import pe_parse
from pe.core import core
from core import support
from core import enum
import os
import logging
import sys
from common.common import *

logger = logging.getLogger(__name__)


class pebin():

    def __init__(self, argparse_options):
        logger.debug("In file {0}".format(str(os.path.abspath(__file__))))
        self.options = argparse_options
        self.set_bools()
        self.result = False
        self.PATCHING_PATH = os.path.abspath(os.path.dirname(__file__)) + \
            '/pe/patching/methods'
        self.MODE_PATH = os.path.abspath(os.path.dirname(__file__)) + \
            '/pe/patching/methods/modes/'
        self.PAYLOAD_PATH = os.path.abspath(os.path.dirname(__file__)) + \
            '/pe/payload/'
        # self.ARCH_PATH = os.path.abspath(os.path.dirname(__file__)) + \
        #  '/pe/core/'
        self.iat_cave_loc = 0
        self.patch_instr = collections.OrderedDict()
        self.curdir = os.path.dirname(__file__)
        self.found_method = False
        self.added_paths = set()

        if 'DISK_OFFSET' not in self.options.keys():
            self.options['DISK_OFFSET'] = 0

        if 'TESTING' in self.options.keys():
            self.TESTING = self.options['TESTING']
        else:
            self.TESTING = False

        if 'TESTING_CAVES' in self.options.keys():
            self.TESTING_CAVES = list(self.options['TESTING_CAVES'].split(','))
        else:
            self.TESTING_CAVES = False

        if 'CAVE_PATTERN' in self.options.keys():
            self.CAVE_PATTERN = bytes.fromhex(self.options['CAVE_PATTERN'])
        else:
            self.CAVE_PATTERN = b'\x00'
        # Add potential options for help output

        self.run()

    # can this be moved to a support file?
    def set_bools(self):
        for key, value in self.options.items():
            if not value:
                continue
            if type(value) != str:
                continue
            if value.lower() == 'true':
                self.options[key] = True
            elif value.lower() == 'false':
                self.options[key] = False
            elif value.lower() == 'none':
                self.options[key] = None

    def dump_parser(self):
        print("=" * 50)
        for key, value in self.pe_object.items():
            if key == 'loaded_binary':
                continue
            else:
                if type(value) == int:
                    print(f'{key}: {hex(value)}')
                else:
                    print(f'{key}: {value}')
                if key == "LCD_CFG_dispatch_fptr":
                    if value != 0x0:
                        print('Has CFG')
        print("=" * 50)

    def run(self):

        # TODO: push a stringIO file
        self.parser = pe_parse.pe_parse(FILE=self.options['b_FILE'],
                                        DISK_OFFSET=self.options['DISK_OFFSET']
                                        )

        if self.parser.run():
            self.pe_object = self.parser.__dict__
            if 'DUMP_PARSER' in self.options:
                if self.options['DUMP_PARSER'] is True:
                    self.dump_parser()
        else:
            logger.error("PE Parser Failure! Wake the DEV! OK don't, but \
                issue a bug.")
            return False

        # Do the patching on the original binary, protect it from modiification
        self.original_binary = io.BytesIO(self.pe_object['loaded_binary'].read(-1))

        self.original_file_hash = hashit('sha256', self.original_binary)

        self.patch_instr['filehash'] = self.original_file_hash

        if 'FILE' in self.options.keys():
            # bdfp sets FILE to None
            if self.options['FILE']:
                self.file_base_name = os.path.basename(self.options['FILE'])
            else:
                self.file_base_name = self.original_file_hash

        self.pe_object['loaded_binary'].seek(0)
        self.added_paths.add(self.PATCHING_PATH)
        self.patch_methods = enum.enum(self.PATCHING_PATH).run()

        if 'PATCH_METHOD' not in self.options.keys():
            support.support(self).print_methods()
            return False

        if not self.options['PATCH_METHOD']:
            support.support(self).print_methods()
            return False

        for patch_method in self.patch_methods:
            if self.options['PATCH_METHOD'].lower() == patch_method.__class__.__name__.lower():
                self.found_method = patch_method
                break

        if not core.core(self).get_magic():
            return False

        # wrong chipset?
        if self.PAYLOAD_PATH_w_CHIP is False:
            self.result = False
            return False

        # wrong patching method?
        if self.found_method is False:
            support.support(self).print_methods()
            self.result = False
            return False

        # PREPROCESS HERE
        # eventually make preprocessing a seperate call
        if self.options['ZERO_CERT']:
            zero = core.core(self)
            zero.remove_code_signing()

        # is this a patch_instr patching process? No? then normal patching
        #   if so, skip CRC32 fixup
        self.patched_binary = self.found_method.patch(self)

        self.result = self.patched_binary

        if not self.result:
            return False

        # POST PROCESS HERE
        # make post processing a seperate call
        # CRC32 checksum update
        if 'CHECKSUM' in self.options.keys():
            # updating the checksum updates the patch_instr
            if self.options['CHECKSUM']:
                checksum = core.core(self)
                checksum.update_checksum()

        # Write out patch_instr w/sha of file if selected

        if 'SAVE_PATCH' in self.options.keys():
            if self.options['SAVE_PATCH']:
                save_patch_instr(self)

        # CODESIGN UPDATE, not part of the patch_instr
        if 'CODESIGN' in self.options.keys():
            if self.options['CODESIGN']:
                codesign = core.core(self)
                codesign.add_code_signing()

        return self.result
