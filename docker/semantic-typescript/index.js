// Semantic analyzer for TypeScript — runs inside codescan/semantic-typescript:latest.
//
// Contract (see harness/knowledge/semantic-analyzer-docker.md):
//   INPUT:  /work mounted read-only (project root containing tsconfig.json or jsconfig.json)
//   OUTPUT: stdout NDJSON, one edge per line
//   EXIT:   0 ok, 1 ts load failure, 2 no tsconfig found, 3 createProgram failure
//
// Special mode: `--self-check` emits a static NDJSON sample + exits 0.

import path from 'node:path';
import fs from 'node:fs';
import process from 'node:process';
import ts from 'typescript';

const TOOL_VERSION = `typescript-${ts.version}`;

function emit(line) { process.stdout.write(line + '\n'); }
function esc(s) { return String(s).replace(/\\/g, '\\\\').replace(/"/g, '\\"'); }
function emitEdge(fromKind, fromName, toKind, toName, rel, line) {
  emit(`{"kind":"edge","from":{"type":"${fromKind}","name":"${esc(fromName)}"},"to":{"type":"${toKind}","name":"${esc(toName)}"},"rel":"${rel}","detail":"semantic","line":${line}}`);
}

function selfCheck() {
  emit(`{"kind":"selfcheck","tool":"${TOOL_VERSION}","ok":true}`);
  emit('{"kind":"edge","from":{"type":"class","name":"DemoChild"},"to":{"type":"type","name":"DemoBase"},"rel":"inherits_or_implements","detail":"semantic","line":1}');
  process.exit(0);
}

function findConfig(workDir) {
  for (const candidate of ['tsconfig.json', 'jsconfig.json']) {
    const full = path.join(workDir, candidate);
    if (fs.existsSync(full)) return full;
  }
  // Fall back to first tsconfig.json anywhere under workDir (shallow).
  try {
    const entries = fs.readdirSync(workDir, { withFileTypes: true });
    for (const e of entries) {
      if (e.isDirectory()) {
        const nested = path.join(workDir, e.name, 'tsconfig.json');
        if (fs.existsSync(nested)) return nested;
      }
    }
  } catch { /* ignore */ }
  return null;
}

function lineOf(node, sourceFile) {
  const pos = node.getStart(sourceFile);
  return sourceFile.getLineAndCharacterOfPosition(pos).line + 1;
}

function analyze(program, checker) {
  for (const sourceFile of program.getSourceFiles()) {
    if (sourceFile.isDeclarationFile) continue;
    if (/\/node_modules\//.test(sourceFile.fileName)) continue;

    const fileName = path.basename(sourceFile.fileName);

    ts.forEachChild(sourceFile, visit);

    function visit(node) {
      // inherits_or_implements — heritage clauses on class/interface
      if (ts.isClassDeclaration(node) || ts.isInterfaceDeclaration(node)) {
        const name = node.name?.text;
        if (name && node.heritageClauses) {
          for (const clause of node.heritageClauses) {
            for (const type of clause.types) {
              const sym = checker.getSymbolAtLocation(type.expression);
              const targetName = sym?.getName() ?? type.expression.getText(sourceFile);
              emitEdge('class', name, 'type', targetName, 'inherits_or_implements', lineOf(node, sourceFile));
            }
          }
        }
      }

      // creates — new T(...)
      if (ts.isNewExpression(node)) {
        const owner = findEnclosingClass(node);
        if (owner) {
          const sym = checker.getSymbolAtLocation(node.expression);
          const targetName = sym?.getName() ?? node.expression.getText(sourceFile);
          emitEdge('class', owner, 'type', targetName, 'creates', lineOf(node, sourceFile));
        }
      }

      // imports — import ... from 'module'
      if (ts.isImportDeclaration(node) && ts.isStringLiteral(node.moduleSpecifier)) {
        emitEdge('file', fileName, 'module', node.moduleSpecifier.text, 'imports', lineOf(node, sourceFile));
      }

      ts.forEachChild(node, visit);
    }
  }
}

function findEnclosingClass(node) {
  let p = node.parent;
  while (p) {
    if (ts.isClassDeclaration(p) && p.name) return p.name.text;
    p = p.parent;
  }
  return null;
}

function main() {
  const argv = process.argv.slice(2);
  if (argv[0] === '--self-check') selfCheck();

  const workDir = argv[0] || '/work';
  if (!fs.existsSync(workDir)) {
    process.stderr.write(`Work directory not found: ${workDir}\n`);
    process.exit(1);
  }

  const configPath = findConfig(workDir);
  if (!configPath) {
    process.stderr.write('No tsconfig.json / jsconfig.json under /work — semantic analysis requires a project model.\n');
    process.exit(2);
  }

  const configFile = ts.readConfigFile(configPath, ts.sys.readFile);
  if (configFile.error) {
    process.stderr.write(`tsconfig read error: ${configFile.error.messageText}\n`);
    process.exit(3);
  }

  const parsed = ts.parseJsonConfigFileContent(configFile.config, ts.sys, path.dirname(configPath));
  if (parsed.errors.length > 0) {
    for (const err of parsed.errors) process.stderr.write(`tsconfig: ${err.messageText}\n`);
    process.exit(3);
  }

  const program = ts.createProgram({ rootNames: parsed.fileNames, options: parsed.options });
  const checker = program.getTypeChecker();
  analyze(program, checker);
  process.exit(0);
}

main();
