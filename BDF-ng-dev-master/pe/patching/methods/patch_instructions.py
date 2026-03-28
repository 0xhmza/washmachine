import os
import logging
from core import enum
from core import support
import io
from common.common import *
logger = logging.getLogger(__name__)


class patch_instructions:

    def __init__(self):
        logger.debug("In file {0}".format(str(os.path.abspath(__file__))))
        self.name = """patch_instructions"""
        self.description = """Patch with a diff, checks hash to match prior file"""
        self.requirements = {'FILE':'Binary to be patched',
                             'PATCH_INSTR':'Previously made instructions for patching a specific file',
                            }

    def support_checks(self):
        support_chk = support.support(self)
        #pre-process here
        if not support_chk.check_reqs():
            return False

        return True

    def patch(self, BDF):
        # self.BDF is passed to inherited classes add class objects if you
        #  want them passed to other classes
        self.BDF = BDF

        # verify sha256

        if not self.support_checks():
            return False

        self.patched_binary = io.BytesIO(self.BDF.original_binary.read(-1))

        self.BDF.patch_instr = return_patch_instr(self.BDF.options['PATCH_INSTR'])

        if not self.BDF.patch_instr:
            return False

        if self.BDF.patch_instr['filehash'] != self.BDF.original_file_hash:
            logger.error('SHA256 hash mismatch in patch_instr')
            return False

        patch = support.support(self)
        patch.patch_file()

        result = self.patched_binary

        result.seek(0)

        return result
