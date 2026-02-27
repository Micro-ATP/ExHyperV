import os
import json
import re

# --- 配置 ---
SOURCE_DIR = r'./'
MAP_FILE = 'translation_map.json'
RESX_DEFAULT = r'./Properties/Resources.resx'
RESX_CN = r'./Properties/Resources.zh-CN.resx'

def xml_escape(text):
    return text.replace('&', '&amp;').replace('<', '&lt;').replace('>', '&gt;').replace('"', '&quot;').replace("'", '&apos;')

def batch_update_resx(mapping):
    """ 一次性批量更新所有资源文件，极大提升速度 """
    for resx_path, lang_key in [(RESX_DEFAULT, 'EN'), (RESX_CN, 'CN')]:
        if not os.path.exists(resx_path):
            print(f"跳过: 找不到 {resx_path}")
            continue

        with open(resx_path, 'r', encoding='utf-8') as f:
            content = f.read()

        new_entries = []
        for item in mapping:
            key = item.get('NewKey')
            val = item.get(lang_key, '')
            if key and f'name="{key}"' not in content:
                new_entries.append(f'  <data name="{key}" xml:space="preserve">\n    <value>{xml_escape(val)}</value>\n  </data>')

        if new_entries:
            new_block = "\n".join(new_entries) + "\n"
            content = content.replace('</root>', f'{new_block}</root>')
            with open(resx_path, 'w', encoding='utf-8') as f:
                f.write(content)
            print(f"  [Resx] 已批量同步 {len(new_entries)} 条翻译到 {os.path.basename(resx_path)}")

def clean_original_text(raw):
    s = raw.strip()
    # 去除首尾的引号
    if s.startswith('"') and s.endswith('"'): s = s[1:-1]
    # 【核心修复】保留双反斜杠，否则文件路径无法匹配
    # 仅将转义的引号还原
    return s.replace('\\"', '"')

def inject_xaml_namespace(content):
    ns = 'xmlns:properties="clr-namespace:ExHyperV.Properties"'
    if "clr-namespace:ExHyperV.Properties" in content: return content
    match = re.search(r'<([\w\d\.]+)', content)
    if match:
        tag_end = match.end(1)
        return content[:tag_end] + f' {ns}' + content[tag_end:]
    return content

def generate_robust_regex(original_text):
    """
    【核心修复】
    将 "虚拟机 {0} 创建成功" 转换为正则 "虚拟机\ \{.*?\}\ 创建成功"
    这样可以完美匹配 C# 代码中的 $"虚拟机 {vmName} 创建成功"
    """
    # 1. 按 {0}, {1}, {0:N2} 等占位符切割字符串
    parts = re.split(r'\{\d+(?::.*?)?\}', original_text)
    
    # 2. 对切割出来的纯文本部分进行安全转义 (处理 . * ? 等正则符号)
    escaped_parts = [re.escape(p) for p in parts]
    
    # 3. 用 \{.*?\} 连接，代表匹配 C# 代码中任意的变量插值
    # \{.*?\} 匹配字面量的 '{' + 非贪婪任意字符 + '}'
    regex_pattern = r'\{.*?\}'.join(escaped_parts)
    
    return regex_pattern

def perform_mega_replace():
    if not os.path.exists(MAP_FILE):
        print(f"错误: 找不到 {MAP_FILE}"); return

    with open(MAP_FILE, 'r', encoding='utf-8') as f:
        mapping = json.load(f)

    # 1. 批量更新 Resx
    print("--- 步骤 1: 批量同步资源文件 ---")
    batch_update_resx(mapping)

    # 2. 准备代码替换
    # 按长度降序排序，防止短字符串误替换长字符串的一部分
    sorted_map = sorted(mapping, key=lambda x: len(clean_original_text(x.get('Original', ''))), reverse=True)

    print("\n--- 步骤 2: 替换源代码 ---")
    files_updated = 0
    exclude = {'.git', 'bin', 'obj', '.vs'}

    for root, dirs, files in os.walk(SOURCE_DIR):
        dirs[:] = [d for d in dirs if d not in exclude]
        for file in files:
            path = os.path.join(root, file)
            is_xaml, is_cs = file.endswith('.xaml'), file.endswith('.cs')
            if not (is_xaml or is_cs): continue

            try:
                with open(path, 'r', encoding='utf-8-sig', errors='ignore') as f:
                    content = f.read()

                new_content = content
                for item in sorted_map:
                    key = item.get('NewKey')
                    raw_val = clean_original_text(item.get('Original', ''))
                    
                    if not key or not raw_val: continue

                    if is_cs:
                        # === C# 替换逻辑 ===
                        
                        # 生成鲁棒的正则表达式
                        regex_pattern = generate_robust_regex(raw_val)
                        
                        # 构造完整的 C# 字符串匹配正则
                        # \$? 匹配可选的 $ 符号
                        # "   匹配开头的引号
                        # ... 内容 ...
                        # "   匹配结尾的引号
                        cs_regex = rf'\$?"{regex_pattern}"'
                        
                        # 构造替换后的代码
                        if item.get('IsFormat'):
                            args = item.get('Args', [])
                            args_str = ", ".join(args) if isinstance(args, list) else str(args)
                            replacement = f"string.Format(Properties.Resources.{key}, {args_str})"
                        else:
                            replacement = f"Properties.Resources.{key}"
                        
                        # 执行替换
                        new_content = re.sub(cs_regex, replacement, new_content)

                    elif is_xaml:
                        # === XAML 替换逻辑 ===
                        new_content = new_content.replace(f'"{raw_val}"', f'"{{x:Static properties:Resources.{key}}}"')
                        new_content = new_content.replace(f"'{raw_val}'", f"'{{x:Static properties:Resources.{key}}}'")

                if new_content != content:
                    if is_xaml: new_content = inject_xaml_namespace(new_content)
                    with open(path, 'w', encoding='utf-8') as f:
                        f.write(new_content)
                    print(f"  [OK] {path}")
                    files_updated += 1

            except Exception as e:
                print(f"  [ERR] {path}: {e}")

    print(f"\n--- 任务完成！共更新 {files_updated} 个文件 ---")

if __name__ == "__main__":
    perform_mega_replace()