#include "bookdesk_fee.h"

int32_t bookdesk_calculate_fee(int32_t days_late, int32_t rate,
    int32_t grace, int32_t cap, int32_t *fee)
{
    if (!fee || days_late < 0 || rate < 0 || grace < 0 || cap < 0)
        return 1;

    int64_t billable = days_late > grace ? (int64_t)days_late - grace : 0;
    int64_t total = billable * rate;
    *fee = total > cap ? cap : (int32_t)total;
    return 0;
}
