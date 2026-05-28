#!/usr/bin/env bash
# scripts/verify-config.sh
# 配置验证脚本 — 验证 SCALE OS 配置完整性

set -e

echo "=== SCALE OS 配置验证 ==="
ERRORS=0

# 检查基础文件
for file in .agent/project.json .agent/report.json .scale/workflow.json; do
    if [ -f "$file" ]; then
        node -e "JSON.parse(require('fs').readFileSync(process.argv[1], 'utf8'))" "$file" 2>/dev/null || { echo "[FAIL] $file JSON invalid"; ERRORS=$((ERRORS + 1)); }
    fi
done

# 检查门控脚本目录
if [ -d "scripts/gates" ]; then
    for hook in scripts/gates/*.sh; do
        if [ -f "$hook" ] && [ ! -x "$hook" ]; then
            echo "[WARN] 不可执行: $hook (自动修复)"
            chmod +x "$hook"
        fi
    done
fi

# 检查 Hook 脚本目录（如存在）
if [ -d ".claude/hooks" ]; then
    REQUIRED_HOOKS="gate-skill-scan gate-lazy-pre detect-lazy-post detect-context-pollution session-start-reminder session-end-gate"
    for hook in $REQUIRED_HOOKS; do
        HOOK_PATH=".claude/hooks/${hook}.sh"
        if [ ! -f "$HOOK_PATH" ]; then
            echo "[WARN] Hook 缺失: ${hook}.sh"
        elif [ ! -x "$HOOK_PATH" ]; then
            echo "[WARN] Hook 不可执行: ${hook}.sh (自动修复)"
            chmod +x "$HOOK_PATH"
        fi
    done

    # 检查 session 目录
    if [ ! -d ".claude/session" ]; then
        mkdir -p .claude/session
    fi
fi

# 检查知识文档
for doc in CLAUDE.md AGENTS.md .cursorrules GEMINI.md; do
    if [ -f "$doc" ]; then
        echo "[OK] 知识文档: $doc"
    fi
done

# 检查 settings.json Hook 配置（Claude Code）
if [ -f ".claude/settings.json" ]; then
    for key in PreToolUse PostToolUse Stop; do
        if grep -q "$key" .claude/settings.json; then
            echo "[OK] settings.json 包含 $key Hook"
        fi
    done
fi

if [ "$ERRORS" -gt 0 ]; then
    echo "[FAIL] 配置验证失败: $ERRORS 个错误"
    exit 1
fi

echo "[OK] 配置验证通过"
exit 0
