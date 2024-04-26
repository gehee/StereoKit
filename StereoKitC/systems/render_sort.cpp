// https://travisdowns.github.io/blog/2019/05/22/sorting.html

#include <string.h>
#include <assert.h>

#include "render_sort.h"

#if _WIN32
#include <intrin.h>
#endif

using namespace sk;

const size_t   RADIX_BITS   = 8;
const size_t   RADIX_SIZE   = (size_t)1 << RADIX_BITS;
const size_t   RADIX_LEVELS = (63 / RADIX_BITS) + 1;
const uint64_t RADIX_MASK   = RADIX_SIZE - 1;

using freq_array_type = size_t [RADIX_LEVELS][RADIX_SIZE];

// Since this sort is specifically for the render queue, we'll reserve a
// chunk of memory that sticks around, and resizes if it's too small.
render_item_t *radix_queue_area = nullptr;
size_t         radix_queue_size = 0;

void radix_sort_clean() {
	free(radix_queue_area);
	radix_queue_area = nullptr;
	radix_queue_size = 0;
}

// never inline just to make it show up easily in profiles (inlining this lengthly function doesn't
// really help anyways)
static void count_frequency(render_item_t *a, size_t count, freq_array_type freqs) {
	for (size_t i = 0; i < count; i++) {
		uint64_t value = a[i].sort_id;
		for (size_t pass = 0; pass < RADIX_LEVELS; pass++) {
			freqs[pass][value & RADIX_MASK]++;
			value >>= RADIX_BITS;
		}
	}
}

/**
* Determine if the frequencies for a given level are "trivial".
* 
* Frequencies are trivial if only a single frequency has non-zero
* occurrences. In that case, the radix step just acts as a copy so we can
* skip it.
*/
static bool is_trivial(size_t freqs[RADIX_SIZE], size_t count) {
	for (size_t i = 0; i < RADIX_SIZE; i++) {
		size_t freq = freqs[i];
		if (freq != 0) {
			return freq == count;
		}
	}
	assert(count == 0); // we only get here if count was zero
	return true;
}

void radix_sort7(render_item_t *a, size_t count) {
	// Resize up if needed
	if (radix_queue_size < count) {
		free(radix_queue_area);
		radix_queue_area = sk_malloc_t(render_item_t, count);
		radix_queue_size = count;
	}
	freq_array_type freqs = {};
	count_frequency(a, count, freqs);

	render_item_t *from = a, *to = radix_queue_area;

	for (size_t pass = 0; pass < RADIX_LEVELS; pass++) {

		if (is_trivial(freqs[pass], count)) {
			// this pass would do nothing, just skip it
			continue;
		}

		uint64_t shift = pass * RADIX_BITS;

		// array of pointers to the current position in each queue, which we set up based on the
		// known final sizes of each queue (i.e., "tighly packed")
		render_item_t *queue_ptrs[RADIX_SIZE], *next = to;
		for (size_t i = 0; i < RADIX_SIZE; i++) {
			queue_ptrs[i] = next;
			next += freqs[pass][i];
		}

		// copy each element into the appropriate queue based on the current RADIX_BITS sized
		// "digit" within it
		for (size_t i = 0; i < count; i++) {
			render_item_t value = from[i];
			size_t        index = (value.sort_id >> shift) & RADIX_MASK;
			*queue_ptrs[index]++ = value;
#ifdef _WIN32
#if defined(_M_ARM) || defined(_M_ARM64)
			__prefetch (queue_ptrs[index] + 1);
#elif !defined(WINDOWS_UWP)
			_m_prefetch(queue_ptrs[index] + 1);
#endif
#else
			__builtin_prefetch(queue_ptrs[index] + 1);
#endif
		}

		// swap from and to areas
		render_item_t *tmp = to;
		to   = from;
		from = tmp;
	}

	// because of the last swap, the "from" area has the sorted payload: if it's
	// not the original array "a", do a final copy
	if (from != a) {
		memcpy(a, from, count*sizeof(render_item_t));
	}
}