# AI编程对话记录总结指南

总结历史对话并生成一个 chat summary markdown 文档。
工作开始前要确认自己有 subagent 调用权限，且用户明确了标签名称(tag_name)。

## 构建 AI对话总结步骤（以总结 tag v1.0.2 的聊天为例）

### 1. 导出对话

列出当前项目中所有对话（不包括当前对话），并将导出的markdown放入 `printf("obj/tag_%s", tag_name)` 目录中，按时间排序，新对话排末尾。

使用相关 API 原样导出，禁止修改文件内容。

文件命名伪代码
```c
// agent chat
printf("%03d_%s.md", chat_num, chat_id)
// sub agent chat
printf("%03d_%03d_%s", chat_num, subagent_num, chat_id not sub_chat_id)
```

### 2. 使用 subagent 简化 markdown 并移除非必要文件

For main agent:

1. 该任务完整交给 subagent 自主进行。主 AGENT 只需要传递处理范围，并要求 subagent 按本章节执行即可。
2. 要求每10个对话启动一个 subagent 处理。Subagents 之间要逐个启动，验收再启动下一个，不要并发。

For subagent:

1. 修改各对话 markdown，简化它们。
2. 如果一个 markdown 中，AGENT 没有根据用户要求实现任何 feature 或者进行 BUG 修复，或者想要执行修复但被用户拒绝或中断，那么，移除该 markdown。
3. 在每个文件中合并多轮对话中的用户要求，对任务情况进行总结。
4. 由于多轮对话中用户要求可能是递进的，合并要求时需要关注前后变化，合并结果要反映最终情况。
5. "用户要求（或主代理）"部分改为英文，用1.2.3将每个细分要求分段。
6. 只简化，没有事实核查的义务。
7. 简化文档格式例子：
```markdown
# 初始化 RoamClock Git 仓库

## 用户要求 或 主代理要求

English here...

## Agent 任务规划

## 最终完成情况
```

### 3. 拼接各项目总结

将对话 markdown 整合内容放入 `printf("ai_chats/tag_%s_chat_summary.md", tag_name)` 中。
在各对话标题前补上序号，序号编码规则与文件名类似但不带 chat_id。
由于部分 markdown 被移除，为保证序号连续性，该序号不必与文件编号相同。

总结文档的一级标题使用 `# [项目名] [tag_name] 对话汇总`。各 markdown 整合时标题级别相应降级。

整合完成后，移除对话 markdown 的目录。

### 4. 隐私清理

清理总结 markdown 中不需要的部分：

1. 涉及项目外文件结构的部分（例如 C:\,D:\\..）

2. 涉及隐私、密钥、具体时间、UUID的部分

隐私部分使用 “(AnonymousToken)”替代

### 5. 将用户要求翻译回中文

调用 subagent 将用户要求或主代理要求部分从英文翻译为中文。该 subagent 应该拥有独立上下文，且在不读取项目其他内容的情况下完成翻译。