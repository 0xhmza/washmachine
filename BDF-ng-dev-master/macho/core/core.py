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
        logger.debug("In file {0}".format(str(os.path.abspath(__file__))))
        self.core = incoming

    def get_magic(self):

        if self.core.MagicNumber == '0xfeedface':
            self.core.PAYLOAD_PATH_w_CHIP = self.core.PAYLOAD_PATH + 'x86/'
            self.core.PAYLOAD_PATH_CORE = self.core.PAYLOAD_PATH + 'x86/x86_core/'
            return True

        elif self.core.MagicNumber == '0xfeedfacf' and self.core.CPU_Type == "0x1000007":

            self.core.PAYLOAD_PATH_w_CHIP = self.core.PAYLOAD_PATH + 'x64/'
            self.core.PAYLOAD_PATH_CORE = self.core.PAYLOAD_PATH + 'x64/x64_core/'
            return True

        elif self.core.MagicNumber == '0xfeedfacf' and self.core.CPU_Type == "0x100000c":

            self.core.PAYLOAD_PATH_w_CHIP = self.core.PAYLOAD_PATH + 'arm64e/'
            self.core.PAYLOAD_PATH_CORE = self.core.PAYLOAD_PATH + 'arm64e/arm64e_core/'
            return True

        else:
            logger.error("Unsupported chipset")

            return False
