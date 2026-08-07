import os
import sys

# `engine` is a package under Sentinel\Azimuth; put that on the path so the
# tests import exactly what a caller would.
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))
