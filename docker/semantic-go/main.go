// Semantic analyzer for Go — runs inside codescan/semantic-go:latest.
//
// Contract (see harness/knowledge/semantic-analyzer-docker.md):
//   INPUT:  /work mounted read-only (project root containing go.mod / go.work)
//   OUTPUT: stdout NDJSON, one edge per line
//   EXIT:   0 ok, 1 invalid input, 2 not implemented yet
//
// Special mode: --self-check emits a static NDJSON sample + exits 0.
//
// Phase 2-B plan: use golang.org/x/tools/go/packages + go/types to resolve
// constructor-function calls (`pkg.NewType()`) into `creates` edges — the regex
// strategy can't follow these because Go has no `new T()` syntax for them.
package main

import (
	"fmt"
	"os"
)

const toolVersion = "go-stub-0.1"

func emit(s string) {
	fmt.Println(s)
}

func selfCheck() int {
	emit(fmt.Sprintf(`{"kind":"selfcheck","tool":"%s","ok":true}`, toolVersion))
	emit(`{"kind":"edge","from":{"type":"class","name":"DemoChild"},"to":{"type":"type","name":"DemoBase"},"rel":"inherits_or_implements","detail":"semantic","line":1}`)
	return 0
}

func analyze(workDir string) int {
	if info, err := os.Stat(workDir); err != nil || !info.IsDir() {
		fmt.Fprintf(os.Stderr, "Work directory not found: %s\n", workDir)
		return 1
	}

	// Phase 2-B stub: real matcher uses golang.org/x/tools/go/packages.
	fmt.Fprintf(os.Stderr,
		"[%s] Go semantic matcher is not yet implemented. "+
			"Falling back is the host's responsibility.\n", toolVersion)
	return 2
}

func main() {
	args := os.Args[1:]
	if len(args) > 0 && args[0] == "--self-check" {
		os.Exit(selfCheck())
	}
	workDir := "/work"
	if len(args) > 0 {
		workDir = args[0]
	}
	os.Exit(analyze(workDir))
}
