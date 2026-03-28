#!/usr/bin/env python3
"""
    BackdoorFactory Proxy NG (BDFProxy) v1.0 
    Author Joshua Pitts the.midnite.runr 'at' gmail <d ot > com
    Copyright (c) 2013-2021, Joshua Pitts
    All rights reserved.
    For secretsquirrel sponsors only.
    For lawful uses only.
"""

from mitmproxy import http
from mitmproxy import ctx
from mitmproxy.script import concurrent
import io
import pebin, machobin, elfbin
from pe import pe_parse
from configobj import ConfigObj
from common.bdfp_executable_h import executable_handler

import logging
logger = logging.getLogger(__name__)


class content_handler():

    def __init__(self, content):
        self.content = content
        self.parent = None
        self.content_type = None
        self.ar_depth_count = 0
        self.config = {}

        self.supported_exes = {'elf': {'number': b'\x7fELF', 'offset': 0},
                             'pe': {'number': b'MZ', 'offset': 0},
                             'fatfile': {'number': b'\xCA\xFE\xBA\xBE', 'offset': 0},
                             'machox64': {'number': b'\xCF\xFA\xED\xFE', 'offset': 0},
                             'machox86': {'number': b'\xCE\xFA\xED\xFE', 'offset': 0},
                             }
        self.supported_archives = {'gz': {'number': b'\x1f\x8b', 'offset': 0},
                             'bz': {'number': b'BZ', 'offset': 0},
                             'zip': {'number': b'PK\x03\x04', 'offset': 0},
                             'tar': {'number': b'ustar', 'offset': 257},
                             }


    def set_config(self):
        try:

            self.user_config = ConfigObj('proxy.cfg')
            self.targetConfigALL = self.user_config['targets']['ALL']
            self.targetConfigBT = self.user_config['targets']['BinaryTypes']
            self.parse_target_config()
        except Exception as e:
            logger.error("Missing field from config file: {0}".format(e))


    def set_config_archive(self, ar):
        try:
            self.archive_type = ar
            self.archive_denylist = self.user_config[self.archive_type]['denylist']
            self.archive_max_size = int(self.user_config[self.archive_type]['maxSize'])
            self.archive_patch_count = int(self.user_config[self.archive_type]['patchCount'])
            self.archive_params = ar
        except Exception as e:
            raise Exception("Missing {0} section from config file".format(e))

    def parse_target_config(self):
        for key, value in self.targetConfigBT.items():
            self.config[key] = value

        self.config['targetConfigALL'] = {}
        for key, value in self.targetConfigALL.items():
            logger.debug(f'{key}, {value}')

            self.config['targetConfigALL'][key] = value
        
        logger.debug(f'updated: {self.config}')


    def inject_zip(self):
        # unzip to memory
        # for each file, check_content
            # if exe_bin
            #   executable_handler
            #   add file back to zip
        pass

    def inject_tar(self):
        # untar to memory support bz/gz
        # for each file, check_content
            # if exe_bin
            # executable_handler
            # add file back to zip
        pass


    def unpack_archive(self):
        # is zip
        # call zip
        # is tar
        # call tar
        pass

    def check_content(self):

        # what is this file, is it an archive or an exe?
        for bin_type, values in self.supported_archives.items():
            #logger.info(f"test: {self.content[values['offset']:len(values['number'])]}")
            if values['number'] == self.content[values['offset']:len(values['number'])]:
                logger.info(f'Found {bin_type}')
                if not self.parent:
                    self.parent = b'archive'
                    self.content_type = bin_type

        for bin_type, values in self.supported_exes.items():
            if values['number'] == self.content[values['offset']:len(values['number'])]:
                logger.info(f'Found {bin_type}')
                if not self.parent:
                    self.parent = b'exe'
                    self.content_type = bin_type

    def run(self):
        # look at self.content here and determine whether to patch or not.
        self.check_content()
        self.set_config()
        if self.content_type:
            logger.info(f'Hey, Found the following content {self.content_type}, {self.parent}')
        # the default is to not modify content
        if self.parent == b'archive':
            self.content = self.unpack_archive()
        elif self.parent == b'exe':
            logger.info('About to launch executable_handler')
            self.content = executable_handler(self.content, self.content_type, self.config).run()

        return self.content

