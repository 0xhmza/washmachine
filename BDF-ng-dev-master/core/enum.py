import os
import sys
import logging
import re
import importlib.util
import sys
logger = logging.getLogger(__name__)


class enum():
    '''
    Class to enumerate anything into the project whether processors,
    patching methods, encoders... whatever

    '''

    def __init__(self, directory):
        logger.debug("In file {0}".format(str(os.path.abspath(__file__))))
        self.directory = directory
        self.the_enumerated = []
        self.named_enumerated = []

    def run(self):
        ignore = ['__init__.py', '.DS_Store']
        dname = os.path.abspath(self.directory)
        logger.debug(f'dname:{dname}')
        sys.path.append(dname)

        for afile in os.listdir(dname + "/"):

            if afile in ignore:
                continue

            if ".pyc" in afile:
                continue

            if ".py" not in afile:
                continue

            if os.path.isdir(os.path.join(dname, afile)):
                continue

            if len(afile.split(".")) > 2:
                logger.warn("\t[!] Make sure there are no '.' in your filenames: %s" % afile)
                return False

            logger.debug(f'dname:{dname}, afile:{afile}')
            file_path = os.path.join(dname, afile)
            module_name = re.sub(r'.py', '', afile)

            spec = importlib.util.spec_from_file_location(module_name,
                                                          file_path)
            module = importlib.util.module_from_spec(spec)
            sys.modules[module_name] = module
            spec.loader.exec_module(module)

            loaded_module = getattr(module, module_name)
            logger.debug(f'loaded_module: {loaded_module}')

            testing = loaded_module()

            try:
                self.named_enumerated.append(testing.name)
                self.the_enumerated.append(testing)
            except Exception as e:
                logger.error("{0}: {1}".format(e, loaded_module))
                return False

        return self.the_enumerated
