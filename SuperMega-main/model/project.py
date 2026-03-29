import logging

from model.defs import *
from model.payload import Payload
from model.settings import Settings
from model.injectable import Injectable

logger = logging.getLogger("Project")


class Project():
    def __init__(self, settings: Settings):
        self.settings: Settings = settings

        # Set by init()
        self.payload: Payload
        self.injectable: Injectable


    def init(self) -> bool:
        self.payload: Payload = Payload(self.settings.get_payload_path())
        self.injectable: Injectable = Injectable(self.settings.get_inject_exe_in())

        if not self.payload.init():
            return False
        if not self.injectable.init():
            return False
        return True
    
    
    def print(self):
        logger.info("Project Name: {}".format(self.settings.project_name))
        logger.info("Comment: {}".format(self.settings.project_comment))
        logger.info("Settings: {}".format(self.settings.__dict__))
        logger.info("Payload Path: {}".format(self.payload.payload_path))
        logger.info("Injectable Path: {}".format(self.injectable.exe_filepath))


def prepare_project(project_name):
    dst = "{}{}/".format(PATH_WEB_PROJECT, project_name)

    logger.info("-[ Prepare and cleanup project: {}".format(project_name))

    if not os.path.exists(dst):
        os.makedirs(dst)

    # delete all files in dst directory
    for file in os.listdir(dst):
        if file == "project.pickle":
            continue
        if file.startswith("."):
            continue
        if file.endswith(".exe"):
            # keep all exes except:
            if file != "main.exe" and not file.endswith(".infected.exe"):
                continue
        if file.endswith(".dll"):
            # keep all dlls except:
            if not file.endswith(".infected.dll"):
                continue

        os.remove(dst + file)
