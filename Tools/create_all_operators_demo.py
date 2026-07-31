
#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
全算子演示工作流自动创建脚本 - 入口文件
这个脚本会调用 operator_demo 目录下的 main.py
"""

import sys
import os

# Add operator_demo directory to path
script_dir = os.path.dirname(os.path.abspath(__file__))
operator_demo_dir = os.path.join(script_dir, "operator_demo")
sys.path.insert(0, operator_demo_dir)

from operator_demo.main import main

if __name__ == "__main__":
    main()

