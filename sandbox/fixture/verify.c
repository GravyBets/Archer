/* Trusted synthetic runtime smoke fixture. No application source executed. */
#include <errno.h>
#include <fcntl.h>
#include <stdio.h>
#include <stdlib.h>
#include <unistd.h>

int main(void) {
    if (getuid() != 65532 || geteuid() != 65532) return 30;
    if (getenv("OPENAI_API_KEY") || getenv("CONTAINER_HOST") || getenv("GITHUB_TOKEN")) return 30;
    if (getchar() != 0x1f || getchar() != 0x8b) return 30;
    int root = open("/must-not-write", O_CREAT | O_WRONLY, 0600);
    if (root >= 0) { close(root); return 30; }
    int work = open("/work/fixture", O_CREAT | O_WRONLY, 0600);
    if (work < 0) return 30;
    close(work);
    int temp = open("/tmp/fixture", O_CREAT | O_WRONLY, 0600);
    if (temp < 0) return 30;
    close(temp);
    puts("Synthetic non-root, stdin and filesystem checks passed.");
    return 0;
}
