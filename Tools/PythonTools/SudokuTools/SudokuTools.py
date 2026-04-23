"""
数独求解工具
提供图形化界面，支持用户输入并实时生成最优解
"""

import tkinter as tk
from tkinter import messagebox
import copy


class SudokuSolver:
    """数独求解器 - 使用回溯算法"""

    def __init__(self):
        self.board = [[0] * 9 for _ in range(9)]

    def is_valid(self, row, col, num):
        """
        检查在指定位置放置数字是否有效

        参数:
            row: 行索引 (0-8)
            col: 列索引 (0-8)
            num: 要放置的数字 (1-9)

        返回:
            bool: 是否有效
        """
        # 检查行
        if num in self.board[row]:
            return False

        # 检查列
        for i in range(9):
            if self.board[i][col] == num:
                return False

        # 检查3x3宫格
        box_row = (row // 3) * 3
        box_col = (col // 3) * 3
        for i in range(box_row, box_row + 3):
            for j in range(box_col, box_col + 3):
                if self.board[i][j] == num:
                    return False

        return True

    def solve(self):
        """
        使用回溯算法求解数独

        返回:
            bool: 是否找到解
        """
        for row in range(9):
            for col in range(9):
                if self.board[row][col] == 0:
                    for num in range(1, 10):
                        if self.is_valid(row, col, num):
                            self.board[row][col] = num

                            if self.solve():
                                return True

                            self.board[row][col] = 0

                    return False

        return True

    def set_board(self, board):
        """设置数独棋盘"""
        self.board = copy.deepcopy(board)

    def get_solution(self):
        """获取求解结果"""
        return copy.deepcopy(self.board)


class SudokuGUI:
    """数独图形界面"""

    def __init__(self, root):
        self.root = root
        self.root.title("数独求解器 - Aurora Framework")
        self.root.resizable(False, False)

        # 数据存储
        self.cells = [[None] * 9 for _ in range(9)]
        self.user_input = [[0] * 9 for _ in range(9)]
        self.solution = [[0] * 9 for _ in range(9)]

        self.solver = SudokuSolver()

        # 创建界面
        self._create_widgets()

    def _create_widgets(self):
        """创建界面组件"""
        # 主框架
        main_frame = tk.Frame(self.root, bg='white', padx=20, pady=20)
        main_frame.pack()

        # 标题
        title_label = tk.Label(
            main_frame,
            text="数独求解器",
            font=("微软雅黑", 20, "bold"),
            bg='white',
            fg='#2c3e50'
        )
        title_label.grid(row=0, column=0, columnspan=9, pady=(0, 20))

        # 数独网格框架
        grid_frame = tk.Frame(main_frame, bg='#34495e', padx=2, pady=2)
        grid_frame.grid(row=1, column=0, columnspan=9)

        # 创建9x9格子
        for row in range(9):
            for col in range(9):
                # 确定边框粗细（3x3宫格边界加粗）
                border_width = 1
                if row % 3 == 0:
                    pady_top = 2
                else:
                    pady_top = 0

                if col % 3 == 0:
                    padx_left = 2
                else:
                    padx_left = 0

                # 创建单元格框架
                cell_frame = tk.Frame(grid_frame, bg='#34495e')
                cell_frame.grid(row=row, column=col, padx=(padx_left, 0), pady=(pady_top, 0))

                # 创建输入框
                cell = tk.Entry(
                    cell_frame,
                    width=3,
                    font=("Arial", 18, "bold"),
                    justify='center',
                    bd=1,
                    relief='solid'
                )
                cell.pack()

                # 绑定事件
                cell.bind('<KeyRelease>', lambda e, r=row, c=col: self._on_cell_change(r, c))
                cell.bind('<FocusIn>', lambda e, r=row, c=col: self._on_cell_focus(r, c))

                self.cells[row][col] = cell

        # 按钮框架
        button_frame = tk.Frame(main_frame, bg='white')
        button_frame.grid(row=2, column=0, columnspan=9, pady=(20, 0))

        # 求解按钮
        solve_btn = tk.Button(
            button_frame,
            text="求解",
            font=("微软雅黑", 12, "bold"),
            bg='#27ae60',
            fg='white',
            width=10,
            height=1,
            relief='flat',
            cursor='hand2',
            command=self._solve_sudoku
        )
        solve_btn.pack(side='left', padx=5)

        # 清空按钮
        clear_btn = tk.Button(
            button_frame,
            text="清空",
            font=("微软雅黑", 12, "bold"),
            bg='#e74c3c',
            fg='white',
            width=10,
            height=1,
            relief='flat',
            cursor='hand2',
            command=self._clear_board
        )
        clear_btn.pack(side='left', padx=5)

        # 示例按钮
        example_btn = tk.Button(
            button_frame,
            text="加载示例",
            font=("微软雅黑", 12, "bold"),
            bg='#3498db',
            fg='white',
            width=10,
            height=1,
            relief='flat',
            cursor='hand2',
            command=self._load_example
        )
        example_btn.pack(side='left', padx=5)

        # 状态标签
        self.status_label = tk.Label(
            main_frame,
            text="请输入数独题目，系统将实时生成解答",
            font=("微软雅黑", 10),
            bg='white',
            fg='#7f8c8d'
        )
        self.status_label.grid(row=3, column=0, columnspan=9, pady=(10, 0))

    def _on_cell_change(self, row, col):
        """单元格内容改变时的回调"""
        cell = self.cells[row][col]
        value = cell.get().strip()

        # 验证输入
        if value == '':
            self.user_input[row][col] = 0
            cell.config(bg='white', fg='black')
        elif value.isdigit() and 1 <= int(value) <= 9:
            num = int(value)
            self.user_input[row][col] = num

            # 验证是否符合数独规则
            if self._is_valid_input(row, col, num):
                cell.config(bg='white', fg='#2c3e50')
                self._update_solution()
            else:
                cell.config(bg='#ffe6e6', fg='#e74c3c')
                self.status_label.config(text="⚠️ 当前输入违反数独规则", fg='#e74c3c')
        else:
            cell.delete(0, tk.END)
            self.user_input[row][col] = 0
            cell.config(bg='white', fg='black')

    def _on_cell_focus(self, row, col):
        """单元格获得焦点时的回调"""
        cell = self.cells[row][col]
        if self.user_input[row][col] == 0:
            cell.config(bg='#ecf0f1')

    def _is_valid_input(self, row, col, num):
        """检查用户输入是否有效"""
        # 临时移除当前位置的值
        original = self.user_input[row][col]
        self.user_input[row][col] = 0

        # 检查行
        if num in self.user_input[row]:
            self.user_input[row][col] = original
            return False

        # 检查列
        for i in range(9):
            if self.user_input[i][col] == num:
                self.user_input[row][col] = original
                return False

        # 检查3x3宫格
        box_row = (row // 3) * 3
        box_col = (col // 3) * 3
        for i in range(box_row, box_row + 3):
            for j in range(box_col, box_col + 3):
                if self.user_input[i][j] == num:
                    self.user_input[row][col] = original
                    return False

        self.user_input[row][col] = original
        return True

    def _update_solution(self):
        """实时更新解答"""
        # 复制用户输入
        self.solver.set_board(self.user_input)

        # 求解
        if self.solver.solve():
            self.solution = self.solver.get_solution()

            # 显示解答（用浅色显示系统生成的数字）
            for row in range(9):
                for col in range(9):
                    if self.user_input[row][col] == 0:
                        self.cells[row][col].delete(0, tk.END)
                        self.cells[row][col].insert(0, str(self.solution[row][col]))
                        self.cells[row][col].config(bg='#e8f5e9', fg='#27ae60')

            self.status_label.config(text="✓ 已生成最优解", fg='#27ae60')
        else:
            # 清除非用户输入的格子
            for row in range(9):
                for col in range(9):
                    if self.user_input[row][col] == 0:
                        self.cells[row][col].delete(0, tk.END)
                        self.cells[row][col].config(bg='white', fg='black')

            self.status_label.config(text="当前题目无解或需要更多线索", fg='#f39c12')

    def _solve_sudoku(self):
        """求解按钮回调"""
        # 将解答填入所有格子
        for row in range(9):
            for col in range(9):
                if self.solution[row][col] != 0:
                    self.cells[row][col].delete(0, tk.END)
                    self.cells[row][col].insert(0, str(self.solution[row][col]))

                    if self.user_input[row][col] == 0:
                        self.cells[row][col].config(bg='#bbdefb', fg='#1976d2')
                    else:
                        self.cells[row][col].config(bg='white', fg='#2c3e50')

        self.status_label.config(text="✓ 已显示完整解答", fg='#1976d2')

    def _clear_board(self):
        """清空棋盘"""
        for row in range(9):
            for col in range(9):
                self.cells[row][col].delete(0, tk.END)
                self.cells[row][col].config(bg='white', fg='black')
                self.user_input[row][col] = 0
                self.solution[row][col] = 0

        self.status_label.config(text="请输入数独题目，系统将实时生成解答", fg='#7f8c8d')

    def _load_example(self):
        """加载示例题目"""
        example = [
            [5, 3, 0, 0, 7, 0, 0, 0, 0],
            [6, 0, 0, 1, 9, 5, 0, 0, 0],
            [0, 9, 8, 0, 0, 0, 0, 6, 0],
            [8, 0, 0, 0, 6, 0, 0, 0, 3],
            [4, 0, 0, 8, 0, 3, 0, 0, 1],
            [7, 0, 0, 0, 2, 0, 0, 0, 6],
            [0, 6, 0, 0, 0, 0, 2, 8, 0],
            [0, 0, 0, 4, 1, 9, 0, 0, 5],
            [0, 0, 0, 0, 8, 0, 0, 7, 9]
        ]

        self._clear_board()

        for row in range(9):
            for col in range(9):
                if example[row][col] != 0:
                    self.cells[row][col].insert(0, str(example[row][col]))
                    self.user_input[row][col] = example[row][col]
                    self.cells[row][col].config(bg='white', fg='#2c3e50')

        self._update_solution()


def main():
    """主函数"""
    root = tk.Tk()
    app = SudokuGUI(root)
    root.mainloop()


if __name__ == "__main__":
    main()
