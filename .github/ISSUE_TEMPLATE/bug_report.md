name: "Bug 反馈"
description: "遇见了Bug"
labels: [· Bug, 新提交]
body:
- type: checkboxes
  id: "yml-1"
  attributes:
    label: "检查项"
    description: "请逐个检查下列项目，并勾选确认。"
    options:
    - label: "我已在 [Issues 页面](https://github.com/MEngYangX/MEngZy/issues) 中搜索，确认了这一 Bug 未被提交过。"
      required: true
- type: textarea
  id: "yml-2"
  attributes:
    label: 描述
    description: "详细描述该 Bug 的具体表现。"
  validations:
    required: true
- type: textarea
  id: "yml-3"
  attributes:
    label: 重现步骤
    description: "描述出现 Bug 的步骤。"
    value: |
      1、
      2、
  validations:
    required: true
- type: textarea
  id: "yml-4"
  attributes:
    label: 日志与附件
    description: "上传服务器日志"
  validations:
    required: true
