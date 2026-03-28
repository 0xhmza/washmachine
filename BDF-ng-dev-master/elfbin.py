from elf import elf_parse
from elf.core import core
from core import support
from core import enum
import os
import logging
import collections
import io
from common.common import *
logger = logging.getLogger(__name__)


class elfbin():

    def __init__(self, argparse_options):
        logger.debug("In file {0}".format(str(os.path.abspath(__file__))))
        self.options = argparse_options
        self.set_bools()
        self.result = False
        self.PATCHING_PATH = os.path.abspath(os.path.dirname(__file__)) + '/elf/patching/methods'
        self.MODE_PATH = os.path.abspath(os.path.dirname(__file__)) + '/elf/patching/methods/modes/'
        self.PAYLOAD_PATH = os.path.abspath(os.path.dirname(__file__)) + '/elf/payload/'
        #self.ARCH_PATH = os.path.abspath(os.path.dirname(__file__)) + '/elf/core/'
        self.patch_instr = collections.OrderedDict()
        self.curdir = os.path.dirname(__file__)
        self.found_method = False

        self.run()

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

    def run(self):

        self.parser = elf_parse.elf_parse(FILE=self.options['b_FILE'])

        if self.parser.run():
            self.elf_object = self.parser.__dict__
        else:
            logger.error("ELF Parser Failure! Wake the DEV! OK don't, but issue a bug.")
            return False

        # ET_EXEC (2) vs ET_DYN this drives methods and modes
        self.original_binary = io.BytesIO(self.elf_object['loaded_binary'].read(-1))

        self.original_file_hash = hashit('sha256', self.original_binary)
        self.patch_instr['filehash'] = self.original_file_hash

        if 'FILE' in self.options.keys():
            if self.options['FILE']:
                self.file_base_name = os.path.basename(self.options['FILE'])
            else:
                self.file_base_name = self.original_file_hash

        self.elf_object['loaded_binary'].seek(0)

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

        # get chipset and magic
        if not core.core(self).get_magic():
            logger.error('Failed getting magic')
            return False

        if self.PAYLOAD_PATH_w_CHIP is False:
            self.result = False
            return False

        if self.found_method is False:
            support.support(self).print_methods()
            self.result = False
            return False

        # do the patching method
        self.result = self.found_method.patch(self)

        if not self.result:
            return False
        # write patching instructions

        if 'SAVE_PATCH' in self.options.keys():
            if self.options['SAVE_PATCH']:
                save_patch_instr(self)

        return self.result
