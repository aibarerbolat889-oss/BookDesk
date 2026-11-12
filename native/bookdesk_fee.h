#ifndef BOOKDESK_FEE_H
#define BOOKDESK_FEE_H

#include <stdint.h>

#ifdef _WIN32
#define BOOKDESK_EXPORT __declspec(dllexport)
#else
#define BOOKDESK_EXPORT
#endif

/* Returns 0 on success and 1 for invalid arguments. */
BOOKDESK_EXPORT int32_t bookdesk_calculate_fee(int32_t days_late, int32_t rate,
    int32_t grace, int32_t cap, int32_t *fee);

#endif
