// Compile against pinned MIT-licensed phoboslab/qoi qoi.h; fixture verification only.
#define QOI_IMPLEMENTATION
#include "qoi.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
int main(int argc, char **argv) {
    if (argc != 6) return 2;
    const int width = atoi(argv[3]), height = atoi(argv[4]), channels = atoi(argv[5]);
    FILE *in = fopen(argv[1], "rb"); if (!in) return 3;
    size_t size = (size_t)width * height * channels;
    unsigned char *pixels = malloc(size); if (!pixels) return 4;
    if (fread(pixels, 1, size, in) != size || fgetc(in) != EOF) return 5;
    fclose(in);
    qoi_desc desc = {(unsigned int)width, (unsigned int)height, (unsigned char)channels, QOI_SRGB};
    if (!qoi_write(argv[2], pixels, &desc)) return 6;
    qoi_desc result;
    unsigned char *decoded = qoi_read(argv[2], &result, channels);
    int valid = decoded && result.width == (unsigned int)width && result.height == (unsigned int)height && !memcmp(pixels, decoded, size);
    free(decoded); free(pixels); return valid ? 0 : 7;
}
